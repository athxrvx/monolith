use std::sync::OnceLock;

use tokio::sync::mpsc::UnboundedSender;
use windows_sys::Win32::{
  Foundation::{HWND, POINT, RECT},
  UI::{
    Accessibility::{SetWinEventHook, UnhookWinEvent, HWINEVENTHOOK},
    Input::KeyboardAndMouse::GetAsyncKeyState,
    WindowsAndMessaging::{
      CallNextHookEx, DispatchMessageW, GetCursorPos, GetForegroundWindow,
      GetMessageW, GetWindowRect, SetWindowsHookExW, TranslateMessage,
      UnhookWindowsHookEx, HHOOK, MSG, MSLLHOOKSTRUCT, WH_MOUSE_LL,
      WM_LBUTTONDOWN, WM_MOUSEWHEEL,
    },
  },
};

use crate::tiling::Rect;

const EVENT_SYSTEM_MOVESIZESTART: u32 = 0x000A;
const EVENT_SYSTEM_MOVESIZEEND: u32 = 0x000B;
const WINEVENT_OUTOFCONTEXT: u32 = 0x0000;

static EVENT_SENDER: OnceLock<
  UnboundedSender<(isize, bool, Rect, i32, i32)>,
> = OnceLock::new();
static POINTER_SENDER: OnceLock<UnboundedSender<PointerInput>> =
  OnceLock::new();

#[derive(Debug, Clone, Copy)]
pub enum PointerInput {
  Wheel { hwnd: isize, direction: i32 },
  EdgeClick { hwnd: isize, x: i32, y: i32 },
}

unsafe extern "system" fn low_level_mouse_hook(
  code: i32,
  wparam: usize,
  lparam: isize,
) -> isize {
  if code >= 0 && lparam != 0 {
    let event = wparam as u32;
    let data = &*(lparam as *const MSLLHOOKSTRUCT);
    let alt_down = GetAsyncKeyState(0x12) < 0;
    if event == WM_MOUSEWHEEL && alt_down {
      let delta = ((data.mouseData >> 16) as u16 as i16) as i32;
      if delta != 0 {
        if let Some(sender) = POINTER_SENDER.get() {
          let _ = sender.send(PointerInput::Wheel {
            hwnd: GetForegroundWindow() as isize,
            direction: delta.signum(),
          });
        }
      }
      return 1;
    }
    if event == WM_LBUTTONDOWN {
      if let Some(sender) = POINTER_SENDER.get() {
        let _ = sender.send(PointerInput::EdgeClick {
          hwnd: GetForegroundWindow() as isize,
          x: data.pt.x,
          y: data.pt.y,
        });
      }
    }
  }
  CallNextHookEx(
    std::ptr::null_mut::<core::ffi::c_void>() as HHOOK,
    code,
    wparam,
    lparam,
  )
}

type WinEventProc = Option<
  unsafe extern "system" fn(HWINEVENTHOOK, u32, HWND, i32, i32, u32, u32),
>;

unsafe extern "system" fn on_move_size(
  _hook: HWINEVENTHOOK,
  event: u32,
  hwnd: HWND,
  _object_id: i32,
  _child_id: i32,
  _thread_id: u32,
  _event_time: u32,
) {
  if hwnd.is_null() {
    return;
  }
  let mut rect: RECT = std::mem::zeroed();
  if GetWindowRect(hwnd, &mut rect) == 0 {
    return;
  }
  let snapshot = Rect::new(
    rect.left,
    rect.top,
    rect.right - rect.left,
    rect.bottom - rect.top,
  );
  let mut cursor: POINT = std::mem::zeroed();
  if GetCursorPos(&mut cursor) == 0 {
    return;
  }
  if let Some(sender) = EVENT_SENDER.get() {
    let _ = sender.send((
      hwnd as isize,
      event == EVENT_SYSTEM_MOVESIZESTART,
      snapshot,
      cursor.x,
      cursor.y,
    ));
  }
}

/// Installs the out-of-context WinEvent hook on a thread with a Windows
/// message loop. The hook only reports move/size begin and end; layout
/// stays on the engine's normal frame loop.
pub fn start_move_size_listener(
  sender: UnboundedSender<(isize, bool, Rect, i32, i32)>,
  pointer_sender: UnboundedSender<PointerInput>,
) {
  std::thread::spawn(move || {
    let _ = EVENT_SENDER.set(sender);
    let _ = POINTER_SENDER.set(pointer_sender);
    let callback: WinEventProc = Some(on_move_size);
    let hook = unsafe {
      SetWinEventHook(
        EVENT_SYSTEM_MOVESIZESTART,
        EVENT_SYSTEM_MOVESIZEEND,
        std::ptr::null_mut(),
        callback,
        0,
        0,
        WINEVENT_OUTOFCONTEXT,
      )
    };
    let mouse_hook = unsafe {
      SetWindowsHookExW(
        WH_MOUSE_LL,
        Some(low_level_mouse_hook),
        std::ptr::null_mut(),
        0,
      )
    };
    if hook.is_null() && mouse_hook.is_null() {
      return;
    }

    let mut message: MSG = unsafe { std::mem::zeroed() };
    while unsafe { GetMessageW(&mut message, std::ptr::null_mut(), 0, 0) }
      > 0
    {
      unsafe {
        TranslateMessage(&message);
        DispatchMessageW(&message);
      }
    }
    if !mouse_hook.is_null() {
      unsafe {
        UnhookWindowsHookEx(mouse_hook);
      }
    }
    if !hook.is_null() {
      unsafe {
        UnhookWinEvent(hook);
      }
    }
  });
}

