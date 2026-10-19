use std::{
  collections::HashSet, ffi::OsString, os::windows::ffi::OsStringExt,
};

use windows_sys::Win32::{
  Foundation::{BOOL, HWND, LPARAM, POINT, RECT},
  Graphics::{
    Dwm::DwmGetWindowAttribute,
    Gdi::{
      GetMonitorInfoW, MonitorFromPoint, MonitorFromWindow, MONITORINFO,
      MONITOR_DEFAULTTONEAREST,
    },
  },
  System::Threading::{
    OpenProcess, QueryFullProcessImageNameW, PROCESS_NAME_WIN32,
    PROCESS_QUERY_LIMITED_INFORMATION,
  },
  UI::WindowsAndMessaging::{
    BeginDeferWindowPos, DeferWindowPos, EndDeferWindowPos, EnumWindows,
    GetClassNameW, GetWindow, GetWindowLongPtrW, GetWindowRect,
    GetWindowTextLengthW, GetWindowTextW, GetWindowThreadProcessId,
    IsIconic, IsWindow, IsWindowVisible, IsZoomed, SetWindowLongPtrW,
    SetWindowPos, ShowWindow, GWL_EXSTYLE, GWL_STYLE, GW_OWNER, HDWP,
    SWP_NOACTIVATE, SWP_NOOWNERZORDER, SWP_NOZORDER, SW_RESTORE,
    WS_CAPTION, WS_CHILD, WS_EX_TOOLWINDOW, WS_THICKFRAME,
  },
};

use crate::{
  config::{EngineConfig, WindowRuleConfig},
  tiling::Rect,
};

pub const DWMWA_CLOAKED: u32 = 14;
const DWMWA_EXTENDED_FRAME_BOUNDS: u32 = 9;

#[derive(Debug, Clone)]
#[allow(dead_code)]
pub struct ManagedWindow {
  pub hwnd: HWND,
  pub title: String,
  pub class_name: String,
  pub process_name: String,
  pub is_floating: bool,
  pub is_visible: bool,
  pub is_minimized: bool,
  pub original_style: isize,
}

pub struct WindowManager {
  pub windows: Vec<ManagedWindow>,
  pub managed_handles: HashSet<isize>,
  pub floating_windows: HashSet<isize>,
  scan_hwnds: Vec<HWND>,
  next_windows: Vec<ManagedWindow>,
  alive_handles: HashSet<isize>,
}

impl WindowManager {
  pub fn new() -> Self {
    Self {
      windows: Vec::new(),
      managed_handles: HashSet::new(),
      floating_windows: HashSet::new(),
      scan_hwnds: Vec::with_capacity(64),
      next_windows: Vec::with_capacity(32),
      alive_handles: HashSet::with_capacity(32),
    }
  }

  /// Scans desktop for candidate manageable windows
  pub fn scan_windows(&mut self, config: &EngineConfig) {
    self.scan_hwnds.clear();

    unsafe extern "system" fn enum_proc(
      hwnd: HWND,
      lparam: LPARAM,
    ) -> BOOL {
      let list = &mut *(lparam as *mut Vec<HWND>);
      list.push(hwnd);
      1
    }

    unsafe {
      EnumWindows(
        Some(enum_proc),
        &mut self.scan_hwnds as *mut _ as LPARAM,
      );
    }

    let mut hwnds = std::mem::take(&mut self.scan_hwnds);
    let mut next_windows = std::mem::take(&mut self.next_windows);
    next_windows.clear();
    let mut alive_handles = std::mem::take(&mut self.alive_handles);
    alive_handles.clear();

    for hwnd in hwnds.drain(..) {
      let handle = hwnd as isize;
      let previously_managed = self.managed_handles.contains(&handle);

      // EnumWindows includes hidden top-level windows. Keep windows we
      // already manage so workspace switches can restore them, while
      // still ignoring unrelated hidden UI.
      if !previously_managed && !is_manageable_window(hwnd) {
        continue;
      }
      if previously_managed && unsafe { IsWindow(hwnd) } == 0 {
        self.managed_handles.remove(&handle);
        self.floating_windows.remove(&handle);
        continue;
      }

      alive_handles.insert(handle);
      let title = get_window_title(hwnd);
      let class_name = get_window_class(hwnd);
      let process_name = get_window_process_name(hwnd);
      let style = unsafe { GetWindowLongPtrW(hwnd, GWL_STYLE) };

      // Check if user manually toggled floating for this hwnd
      let mut is_floating = self.floating_windows.contains(&handle);

      // Check if matches rule
      let (rule_floating, hide_border) = evaluate_rules(
        &title,
        &class_name,
        &process_name,
        &config.window_rules,
      );

      if let Some(rf) = rule_floating {
        is_floating = rf;
      }

      if hide_border {
        unsafe {
          let stripped =
            style & !(WS_CAPTION as isize | WS_THICKFRAME as isize);
          SetWindowLongPtrW(hwnd, GWL_STYLE, stripped);
        }
      }

      let is_visible =
        unsafe { IsWindowVisible(hwnd) != 0 } && !is_window_cloaked(hwnd);
      let is_minimized = unsafe { IsIconic(hwnd) != 0 };
      next_windows.push(ManagedWindow {
        hwnd,
        title,
        class_name,
        process_name,
        is_floating,
        is_visible,
        is_minimized,
        original_style: style,
      });
    }

    // Drop stale entries when an application closes, but retain
    // intentionally hidden windows.
    self
      .managed_handles
      .retain(|handle| alive_handles.contains(handle));
    self.managed_handles.extend(alive_handles.iter().copied());
    self
      .floating_windows
      .retain(|handle| alive_handles.contains(handle));
    // Swap in the refreshed records and retain both vectors' capacity for
    // the next scan rather than allocating a fresh pair every 200 ms.
    std::mem::swap(&mut self.windows, &mut next_windows);
    next_windows.clear();
    hwnds.clear();
    self.scan_hwnds = hwnds;
    self.next_windows = next_windows;
    self.alive_handles = alive_handles;
  }

  pub fn window_rect(&self, hwnd: HWND) -> Option<Rect> {
    unsafe {
      if IsZoomed(hwnd) != 0 {
        ShowWindow(hwnd, SW_RESTORE);
      }
      visible_window_rect(hwnd)
    }
  }

  /// Reflows all tiled windows on screen using hardware-accelerated batch
  /// DeferWindowPos. Skips any windows that are already in position to
  /// eliminate jitter and flickering.
  pub fn apply_layout(&self, rects: &[(HWND, Rect)]) {
    if rects.is_empty() {
      return;
    }

    let mut to_move: Vec<(HWND, Rect)> = Vec::new();

    for &(hwnd, r) in rects {
      unsafe {
        // Restore a maximized window only when it is about to be tiled.
        // Doing this during discovery caused every 200ms scan to
        // fight the user's maximize action.
        if IsZoomed(hwnd) != 0 {
          ShowWindow(hwnd, SW_RESTORE);
        }

        if let Some(current) = visible_window_rect(hwnd) {
          if current == r {
            continue;
          }
        }
        to_move.push((hwnd, position_for_visible_rect(hwnd, r)));
      }
    }

    if to_move.is_empty() {
      return;
    }

    unsafe {
      let mut hdwp: HDWP = BeginDeferWindowPos(to_move.len() as i32);
      if hdwp.is_null() {
        for &(hwnd, r) in &to_move {
          SetWindowPos(
            hwnd,
            std::ptr::null_mut(),
            r.x,
            r.y,
            r.width,
            r.height,
            SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOOWNERZORDER,
          );
        }
        return;
      }

      for &(hwnd, r) in &to_move {
        hdwp = DeferWindowPos(
          hdwp,
          hwnd,
          std::ptr::null_mut(),
          r.x,
          r.y,
          r.width,
          r.height,
          SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOOWNERZORDER,
        );
        if hdwp.is_null() {
          break;
        }
      }

      if !hdwp.is_null() {
        EndDeferWindowPos(hdwp);
      }
    }
  }
}

/// DWM's extended frame bounds exclude the invisible resize border
/// returned by GetWindowRect. Layout gaps are defined between visible
/// frames, so convert the desired visible rectangle back to the HWND
/// rectangle for SetWindowPos.
fn visible_window_rect(hwnd: HWND) -> Option<Rect> {
  unsafe {
    let mut rect: RECT = std::mem::zeroed();
    if DwmGetWindowAttribute(
      hwnd,
      DWMWA_EXTENDED_FRAME_BOUNDS,
      &mut rect as *mut _ as *mut std::ffi::c_void,
      std::mem::size_of::<RECT>() as u32,
    ) == 0
    {
      return Some(Rect::new(
        rect.left,
        rect.top,
        rect.right - rect.left,
        rect.bottom - rect.top,
      ));
    }
    if GetWindowRect(hwnd, &mut rect) == 0 {
      return None;
    }
    Some(Rect::new(
      rect.left,
      rect.top,
      rect.right - rect.left,
      rect.bottom - rect.top,
    ))
  }
}

fn position_for_visible_rect(hwnd: HWND, visible: Rect) -> Rect {
  unsafe {
    let mut window: RECT = std::mem::zeroed();
    let mut frame: RECT = std::mem::zeroed();
    if GetWindowRect(hwnd, &mut window) == 0
      || DwmGetWindowAttribute(
        hwnd,
        DWMWA_EXTENDED_FRAME_BOUNDS,
        &mut frame as *mut _ as *mut std::ffi::c_void,
        std::mem::size_of::<RECT>() as u32,
      ) != 0
    {
      return visible;
    }
    let left = frame.left - window.left;
    let top = frame.top - window.top;
    let right = window.right - frame.right;
    let bottom = window.bottom - frame.bottom;
    Rect::new(
      visible.x - left,
      visible.y - top,
      (visible.width + left + right).max(1),
      (visible.height + top + bottom).max(1),
    )
  }
}

/// Comprehensive filter determining if a window is an eligible candidate
/// for tiling
pub fn is_manageable_window(hwnd: HWND) -> bool {
  unsafe {
    // 1. Basic visibility & minimized check
    if IsWindowVisible(hwnd) == 0 {
      return false;
    }
    if IsIconic(hwnd) != 0 {
      return false;
    }

    // 2. Check for cloaked windows (hidden UWP apps or windows on other
    //    virtual desktops)
    let mut cloaked: u32 = 0;
    let hr = DwmGetWindowAttribute(
      hwnd,
      DWMWA_CLOAKED,
      &mut cloaked as *mut _ as *mut std::ffi::c_void,
      std::mem::size_of::<u32>() as u32,
    );
    if hr == 0 && cloaked != 0 {
      return false;
    }

    // 3. Style checks: Child windows and Tool windows are strictly
    //    excluded
    let style = GetWindowLongPtrW(hwnd, GWL_STYLE) as u32;
    if (style & WS_CHILD) != 0 {
      return false;
    }

    let ex_style = GetWindowLongPtrW(hwnd, GWL_EXSTYLE) as u32;
    if (ex_style & WS_EX_TOOLWINDOW) != 0 {
      return false;
    }

    // 4. Exclude owned popup / dialog windows
    let owner = GetWindow(hwnd, GW_OWNER);
    if !owner.is_null() {
      return false;
    }

    // 5. Exclude Monolith components & background shell processes
    let proc = get_window_process_name(hwnd).to_lowercase();
    if proc == "monolith.exe"
      || proc == "monolith_engine.exe"
      || proc == "monolith.studio.exe"
    {
      return false;
    }
    if proc == "searchapp.exe"
      || proc == "searchhost.exe"
      || proc == "startmenuexperiencehost.exe"
      || proc == "shellexperiencehost.exe"
      || proc == "textinputhost.exe"
      || proc == "lockapp.exe"
    {
      return false;
    }

    // 6. Exclude system taskbars, desktop, notification islands, and
    //    overlays
    let class = get_window_class(hwnd);
    let ignored_classes = [
      "Shell_TrayWnd",
      "Shell_SecondaryTrayWnd",
      "Progman",
      "WorkerW",
      "Windows.UI.Core.CoreWindow",
      "TopLevelWindowForOverflowXamlIsland",
      "XamlExplorerHostIslandWindow",
      "NotifyIconOverflowWindow",
      "TaskListThumbnailWnd",
      "TaskListOverlayWnd",
      "DropperWindow",
      "ForegroundStaging",
    ];
    for ign in &ignored_classes {
      if class.eq_ignore_ascii_case(ign) {
        return false;
      }
    }

    // 7. Check non-trivial window rect
    let mut r: RECT = std::mem::zeroed();
    if GetWindowRect(hwnd, &mut r) != 0 {
      if (r.right - r.left) <= 20 || (r.bottom - r.top) <= 20 {
        return false;
      }
    }

    // 8. Title check (ApplicationFrameWindow might initially delay title)
    let title = get_window_title(hwnd);
    if title.is_empty()
      && !class.eq_ignore_ascii_case("ApplicationFrameWindow")
    {
      return false;
    }

    true
  }
}

fn is_window_cloaked(hwnd: HWND) -> bool {
  unsafe {
    let mut cloaked: u32 = 0;
    let result = DwmGetWindowAttribute(
      hwnd,
      DWMWA_CLOAKED,
      &mut cloaked as *mut _ as *mut std::ffi::c_void,
      std::mem::size_of::<u32>() as u32,
    );
    result == 0 && cloaked != 0
  }
}

pub fn get_primary_monitor_rect() -> Rect {
  unsafe {
    let mut mi: MONITORINFO = std::mem::zeroed();
    mi.cbSize = std::mem::size_of::<MONITORINFO>() as u32;

    let hmonitor =
      MonitorFromWindow(std::ptr::null_mut(), MONITOR_DEFAULTTONEAREST);
    if GetMonitorInfoW(hmonitor, &mut mi) != 0 {
      let w = mi.rcWork.right - mi.rcWork.left;
      let h = mi.rcWork.bottom - mi.rcWork.top;
      Rect::new(mi.rcWork.left, mi.rcWork.top, w, h)
    } else {
      Rect::new(0, 0, 1920, 1080)
    }
  }
}

pub fn monitor_for_window(hwnd: HWND) -> isize {
  unsafe { MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST) as isize }
}

pub fn get_monitor_rect_for_window(hwnd: HWND) -> Rect {
  unsafe {
    let mut mi: MONITORINFO = std::mem::zeroed();
    mi.cbSize = std::mem::size_of::<MONITORINFO>() as u32;
    let monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
    if GetMonitorInfoW(monitor, &mut mi) != 0 {
      Rect::new(
        mi.rcWork.left,
        mi.rcWork.top,
        mi.rcWork.right - mi.rcWork.left,
        mi.rcWork.bottom - mi.rcWork.top,
      )
    } else {
      get_primary_monitor_rect()
    }
  }
}

/// Returns the physical monitor bounds and work area containing a pointer
/// location. Edge navigation uses the physical bounds for hit detection and
/// the monitor handle to keep each scrolling strip independent.
pub fn monitor_at_point(x: i32, y: i32) -> Option<(isize, Rect)> {
  unsafe {
    let point = POINT { x, y };
    let monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
    if monitor.is_null() {
      return None;
    }
    let mut info: MONITORINFO = std::mem::zeroed();
    info.cbSize = std::mem::size_of::<MONITORINFO>() as u32;
    if GetMonitorInfoW(monitor, &mut info) == 0 {
      return None;
    }
    Some((
      monitor as isize,
      Rect::new(
        info.rcMonitor.left,
        info.rcMonitor.top,
        info.rcMonitor.right - info.rcMonitor.left,
        info.rcMonitor.bottom - info.rcMonitor.top,
      ),
    ))
  }
}

fn evaluate_rules(
  _title: &str,
  _class: &str,
  process: &str,
  rules: &[WindowRuleConfig],
) -> (Option<bool>, bool) {
  let lower_proc = process.to_lowercase();

  for rule in rules {
    let rule_proc = rule.process_name.to_lowercase();
    if !rule_proc.is_empty()
      && (lower_proc == rule_proc || lower_proc.ends_with(&rule_proc))
    {
      let is_floating = if rule.action.eq_ignore_ascii_case("Force Float")
        || rule.action.eq_ignore_ascii_case("Ignore Tiling")
      {
        Some(true)
      } else if rule.action.eq_ignore_ascii_case("Force Tile") {
        Some(false)
      } else {
        None
      };

      return (is_floating, rule.manage_border_visibility);
    }
  }

  (None, false)
}

pub fn get_window_title(hwnd: HWND) -> String {
  unsafe {
    let len = GetWindowTextLengthW(hwnd);
    if len > 0 {
      let mut buf: Vec<u16> = vec![0; (len + 1) as usize];
      let read = GetWindowTextW(hwnd, buf.as_mut_ptr(), len + 1);
      if read > 0 {
        buf.truncate(read as usize);
        return OsString::from_wide(&buf).to_string_lossy().to_string();
      }
    }
  }
  String::new()
}

pub fn get_window_class(hwnd: HWND) -> String {
  unsafe {
    let mut buf: [u16; 256] = [0; 256];
    let read = GetClassNameW(hwnd, buf.as_mut_ptr(), 256);
    if read > 0 {
      return OsString::from_wide(&buf[..read as usize])
        .to_string_lossy()
        .to_string();
    }
  }
  String::new()
}

pub fn get_window_process_name(hwnd: HWND) -> String {
  unsafe {
    let mut pid: u32 = 0;
    GetWindowThreadProcessId(hwnd, &mut pid);
    if pid == 0 {
      return String::new();
    }

    let handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pid);
    if handle.is_null() {
      return String::new();
    }

    let mut buf: [u16; 1024] = [0; 1024];
    let mut size = 1024u32;
    let success = QueryFullProcessImageNameW(
      handle,
      PROCESS_NAME_WIN32,
      buf.as_mut_ptr(),
      &mut size,
    );
    windows_sys::Win32::Foundation::CloseHandle(handle);

    if success != 0 && size > 0 {
      let full_path = OsString::from_wide(&buf[..size as usize])
        .to_string_lossy()
        .to_string();
      if let Some(name) = std::path::Path::new(&full_path).file_name() {
        return name.to_string_lossy().to_string();
      }
    }
  }
  String::new()
}
