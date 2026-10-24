use std::sync::OnceLock;

use tokio::sync::mpsc::UnboundedSender;
use windows_sys::Win32::{
  Foundation::{HWND, POINT, RECT},
  UI::{
    Accessibility::{SetWinEventHook, UnhookWinEvent, HWINEVENTHOOK},
    WindowsAndMessaging::{
      DispatchMessageW, GetCursorPos, GetMessageW, GetWindowRect,
      TranslateMessage, MSG,
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
) {
  std::thread::spawn(move || {
    let _ = EVENT_SENDER.set(sender);
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
    if hook.is_null() {
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
    if !hook.is_null() {
      unsafe {
        UnhookWinEvent(hook);
      }
    }
  });
}

