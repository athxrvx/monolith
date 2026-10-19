//! Parked implementation of a Komorebi-style focused-window outline.
//!
//! The renderer uses a click-through, non-activating popup owned by the
//! focused application window. A rectangular window region with its center
//! removed gives us a real adjustable-width outline without covering the
//! application's client area. `main.rs` deliberately does not enable this
//! renderer in the current release.

#![allow(dead_code)]

use std::ptr::{null, null_mut};
use std::sync::OnceLock;

use windows_sys::Win32::Foundation::{HWND, RECT};
use windows_sys::Win32::Graphics::Gdi::{
  CombineRgn, CreateRectRgn, CreateSolidBrush, DeleteObject, FillRect,
  GetDC, ReleaseDC, SetWindowRgn, HGDIOBJ, RGN_DIFF,
};
use windows_sys::Win32::System::LibraryLoader::GetModuleHandleW;
use windows_sys::Win32::UI::WindowsAndMessaging::{
  CreateWindowExW, DefWindowProcW, DestroyWindow, RegisterClassExW,
  SetLayeredWindowAttributes, SetWindowPos, ShowWindow,
  WNDCLASSEXW, LWA_ALPHA, SW_HIDE, SWP_NOACTIVATE,
  SWP_NOOWNERZORDER, SWP_SHOWWINDOW, WS_EX_LAYERED, WS_EX_NOACTIVATE,
  WS_EX_TOOLWINDOW, WS_EX_TRANSPARENT, WS_POPUP,
};

const CLASS_NAME: &[u16] = &[
  77, 111, 110, 111, 108, 105, 116, 104, 46, 70, 111, 99, 117, 115, 66, 111,
  114, 100, 101, 114, 0,
];

static CLASS_REGISTERED: OnceLock<bool> = OnceLock::new();

pub struct FocusBorderOverlay {
  hwnd: HWND,
  target: HWND,
  size: (i32, i32),
  thickness: i32,
  alpha: u8,
  color: String,
}

impl FocusBorderOverlay {
  pub fn new() -> Self {
    Self {
      hwnd: null_mut(),
      target: null_mut(),
      size: (0, 0),
      thickness: 0,
      alpha: 0,
      color: String::new(),
    }
  }

  /// Draws or moves the outline. Pass a null HWND to hide it.
  pub fn update(
    &mut self,
    target: HWND,
    color: &str,
    thickness: i32,
  ) {
    if target.is_null() {
      self.hide();
      return;
    }

    let mut target_rect: RECT = unsafe { std::mem::zeroed() };
    if unsafe {
      windows_sys::Win32::UI::WindowsAndMessaging::GetWindowRect(
        target,
        &mut target_rect,
      )
    } == 0
    {
      self.hide();
      return;
    }

    let width = target_rect.right - target_rect.left;
    let height = target_rect.bottom - target_rect.top;
    let thickness = thickness.clamp(1, 8);
    if width <= 0 || height <= 0 {
      self.hide();
      return;
    }

    let Some((rgb, alpha)) = parse_color(color) else {
      self.hide();
      return;
    };

    if self.target != target {
      self.destroy_window();
      if !self.create_window(target, alpha) {
        return;
      }
      self.target = target;
      self.size = (0, 0);
      self.thickness = 0;
      self.alpha = alpha;
      self.color.clear();
    }

    if self.alpha != alpha {
      unsafe {
        SetLayeredWindowAttributes(self.hwnd, 0, alpha, LWA_ALPHA);
      }
      self.alpha = alpha;
    }

    let outer_width = width.saturating_add(thickness * 2).max(1);
    let outer_height = height.saturating_add(thickness * 2).max(1);
    if self.size != (outer_width, outer_height)
      || self.thickness != thickness
    {
      if !self.set_ring_region(outer_width, outer_height, thickness) {
        self.hide();
        return;
      }
      self.size = (outer_width, outer_height);
      self.thickness = thickness;
      self.paint_ring(outer_width, outer_height, rgb);
      self.color.clear();
      self.color.push_str(color);
    }

    if self.color != color {
      unsafe {
        SetLayeredWindowAttributes(self.hwnd, 0, alpha, LWA_ALPHA);
      }
      self.paint_ring(outer_width, outer_height, rgb);
      self.color.clear();
      self.color.push_str(color);
    }

    unsafe {
      // Keep the popup immediately above its owner instead of globally
      // topmost so unrelated dialogs can still appear over the border.
      SetWindowPos(
        self.hwnd,
        target,
        target_rect.left - thickness,
        target_rect.top - thickness,
        outer_width,
        outer_height,
        SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_SHOWWINDOW,
      );
    }
  }

  fn create_window(&mut self, owner: HWND, alpha: u8) -> bool {
    let registered = *CLASS_REGISTERED.get_or_init(|| unsafe {
      let class = WNDCLASSEXW {
        cbSize: std::mem::size_of::<WNDCLASSEXW>() as u32,
        style: 0,
        lpfnWndProc: Some(DefWindowProcW),
        cbClsExtra: 0,
        cbWndExtra: 0,
        hInstance: GetModuleHandleW(null()),
        hIcon: null_mut(),
        hCursor: null_mut(),
        hbrBackground: null_mut(),
        lpszMenuName: null(),
        lpszClassName: CLASS_NAME.as_ptr(),
        hIconSm: null_mut(),
      };
      RegisterClassExW(&class) != 0
    });
    if !registered {
      return false;
    }

    unsafe {
      self.hwnd = CreateWindowExW(
        WS_EX_LAYERED
          | WS_EX_TRANSPARENT
          | WS_EX_TOOLWINDOW
          | WS_EX_NOACTIVATE,
        CLASS_NAME.as_ptr(),
        null(),
        WS_POPUP,
        0,
        0,
        1,
        1,
        owner,
        null_mut(),
        GetModuleHandleW(null()),
        null(),
      );
      if self.hwnd.is_null() {
        return false;
      }
      if SetLayeredWindowAttributes(self.hwnd, 0, alpha, LWA_ALPHA) == 0 {
        DestroyWindow(self.hwnd);
        self.hwnd = null_mut();
        return false;
      }
      true
    }
  }

  fn set_ring_region(
    &self,
    outer_width: i32,
    outer_height: i32,
    thickness: i32,
  ) -> bool {
    unsafe {
      let outer = CreateRectRgn(0, 0, outer_width, outer_height);
      let inner = CreateRectRgn(
        thickness,
        thickness,
        outer_width - thickness,
        outer_height - thickness,
      );
      if outer.is_null() || inner.is_null() {
        if !outer.is_null() {
          DeleteObject(outer as HGDIOBJ);
        }
        if !inner.is_null() {
          DeleteObject(inner as HGDIOBJ);
        }
        return false;
      }

      let combined = CombineRgn(outer, outer, inner, RGN_DIFF);
      DeleteObject(inner as HGDIOBJ);
      if combined == 0 {
        DeleteObject(outer as HGDIOBJ);
        return false;
      }

      // On success Windows owns `outer` and frees it with the window region.
      if SetWindowRgn(self.hwnd, outer, 1) == 0 {
        DeleteObject(outer as HGDIOBJ);
        return false;
      }
      true
    }
  }

  fn paint_ring(&self, width: i32, height: i32, color: u32) {
    unsafe {
      let dc = GetDC(self.hwnd);
      if dc.is_null() {
        return;
      }
      let brush = CreateSolidBrush(color);
      if !brush.is_null() {
        let rect = RECT {
          left: 0,
          top: 0,
          right: width,
          bottom: height,
        };
        // The window region clips this fill to the outer frame ring.
        FillRect(dc, &rect, brush);
        DeleteObject(brush as HGDIOBJ);
      }
      ReleaseDC(self.hwnd, dc);
    }
  }

  fn hide(&self) {
    if !self.hwnd.is_null() {
      unsafe { ShowWindow(self.hwnd, SW_HIDE) };
    }
  }

  fn destroy_window(&mut self) {
    if !self.hwnd.is_null() {
      unsafe { DestroyWindow(self.hwnd) };
      self.hwnd = null_mut();
    }
    self.target = null_mut();
    self.size = (0, 0);
    self.thickness = 0;
    self.alpha = 0;
    self.color.clear();
  }
}

impl Drop for FocusBorderOverlay {
  fn drop(&mut self) {
    self.destroy_window();
  }
}

fn parse_color(value: &str) -> Option<(u32, u8)> {
  let hex = value.strip_prefix('#').unwrap_or(value);
  if !hex.bytes().all(|byte| byte.is_ascii_hexdigit()) {
    return None;
  }
  let (alpha, rgb) = match hex.len() {
    6 => (255, hex),
    8 => {
      let alpha = u8::from_str_radix(&hex[..2], 16).ok()?;
      (alpha, &hex[2..])
    }
    _ => return None,
  };
  let red = u8::from_str_radix(&rgb[0..2], 16).ok()? as u32;
  let green = u8::from_str_radix(&rgb[2..4], 16).ok()? as u32;
  let blue = u8::from_str_radix(&rgb[4..6], 16).ok()? as u32;
  Some((red | (green << 8) | (blue << 16), alpha))
}
