use std::{
  cell::RefCell,
  sync::{
    atomic::{AtomicUsize, Ordering},
    Arc,
  },
};

use windows_sys::{
  core::{GUID, HRESULT},
  Win32::{
    Foundation::{BOOL, HWND, S_OK},
    System::Com::{
      CoCreateInstance, CoInitializeEx, CoUninitialize, CLSCTX_ALL,
      CLSCTX_INPROC_SERVER, COINIT_APARTMENTTHREADED,
    },
  },
};

// Standard IVirtualDesktopManager GUIDs
pub const CLSID_VIRTUAL_DESKTOP_MANAGER: GUID = GUID {
  data1: 0xaa509085,
  data2: 0x0a56,
  data3: 0x4bf3,
  data4: [0xa7, 0xd1, 0x8f, 0x14, 0x22, 0x73, 0x21, 0x77],
};

pub const IID_IVIRTUAL_DESKTOP_MANAGER: GUID = GUID {
  data1: 0xa5cd92ff,
  data2: 0x29be,
  data3: 0x454c,
  data4: [0x8d, 0x04, 0xd8, 0x28, 0x79, 0xfb, 0x3f, 0x1b],
};

#[repr(C)]
pub struct IVirtualDesktopManagerVtbl {
  pub query_interface: unsafe extern "system" fn(
    this: *mut IVirtualDesktopManager,
    riid: *const GUID,
    ppv_object: *mut *mut std::ffi::c_void,
  ) -> HRESULT,
  pub add_ref:
    unsafe extern "system" fn(this: *mut IVirtualDesktopManager) -> u32,
  pub release:
    unsafe extern "system" fn(this: *mut IVirtualDesktopManager) -> u32,
  pub is_window_on_current_virtual_desktop:
    unsafe extern "system" fn(
      this: *mut IVirtualDesktopManager,
      top_level_window: HWND,
      on_current_desktop: *mut BOOL,
    ) -> HRESULT,
  pub get_window_desktop_id: unsafe extern "system" fn(
    this: *mut IVirtualDesktopManager,
    top_level_window: HWND,
    desktop_id: *mut GUID,
  ) -> HRESULT,
  pub move_window_to_desktop: unsafe extern "system" fn(
    this: *mut IVirtualDesktopManager,
    top_level_window: HWND,
    desktop_id: *const GUID,
  ) -> HRESULT,
}

#[repr(C)]
pub struct IVirtualDesktopManager {
  pub vtbl: *const IVirtualDesktopManagerVtbl,
}

thread_local! {
    static COM_INITIALIZED: RefCell<bool> = const { RefCell::new(false) };
}

pub struct VirtualDesktopManager {
  active_workspace: Arc<AtomicUsize>,
  com_manager: *mut IVirtualDesktopManager,
}

unsafe impl Send for VirtualDesktopManager {}
unsafe impl Sync for VirtualDesktopManager {}

#[allow(dead_code)]
impl VirtualDesktopManager {
  pub fn new() -> Self {
    init_com_thread();

    let mut ptr: *mut std::ffi::c_void = std::ptr::null_mut();
    let com_manager = unsafe {
      let hr = CoCreateInstance(
        &CLSID_VIRTUAL_DESKTOP_MANAGER,
        std::ptr::null_mut(),
        CLSCTX_ALL | CLSCTX_INPROC_SERVER,
        &IID_IVIRTUAL_DESKTOP_MANAGER,
        &mut ptr,
      );
      if hr == S_OK {
        ptr as *mut IVirtualDesktopManager
      } else {
        std::ptr::null_mut()
      }
    };

    Self {
      active_workspace: Arc::new(AtomicUsize::new(1)),
      com_manager,
    }
  }

  pub fn get_active_workspace(&self) -> usize {
    self.active_workspace.load(Ordering::SeqCst)
  }

  pub fn set_active_workspace(&self, index: usize) {
    self.active_workspace.store(index, Ordering::SeqCst);
  }

  /// Checks if a window belongs to the current native Windows Virtual
  /// Desktop
  pub fn is_window_on_current_desktop(&self, hwnd: HWND) -> bool {
    if self.com_manager.is_null() {
      return true;
    }

    unsafe {
      let mut on_current: BOOL = 1;
      let vtbl = (*self.com_manager).vtbl;
      let hr = ((*vtbl).is_window_on_current_virtual_desktop)(
        self.com_manager,
        hwnd,
        &mut on_current,
      );

      if hr == S_OK {
        on_current != 0
      } else {
        true
      }
    }
  }

  /// Queries the native GUID of the virtual desktop housing this window
  pub fn get_window_desktop_id(&self, hwnd: HWND) -> Option<GUID> {
    if self.com_manager.is_null() {
      return None;
    }

    unsafe {
      let mut guid: GUID = std::mem::zeroed();
      let vtbl = (*self.com_manager).vtbl;
      let hr =
        ((*vtbl).get_window_desktop_id)(self.com_manager, hwnd, &mut guid);

      if hr == S_OK {
        Some(guid)
      } else {
        None
      }
    }
  }

  /// Moves a window to the targeted native desktop via GUID
  pub fn move_window_to_desktop(
    &self,
    hwnd: HWND,
    desktop_id: &GUID,
  ) -> bool {
    if self.com_manager.is_null() {
      return false;
    }

    unsafe {
      let vtbl = (*self.com_manager).vtbl;
      let hr = ((*vtbl).move_window_to_desktop)(
        self.com_manager,
        hwnd,
        desktop_id,
      );
      hr == S_OK
    }
  }
}

impl Drop for VirtualDesktopManager {
  fn drop(&mut self) {
    if !self.com_manager.is_null() {
      unsafe {
        let vtbl = (*self.com_manager).vtbl;
        ((*vtbl).release)(self.com_manager);
      }
      self.com_manager = std::ptr::null_mut();
    }
  }
}

pub fn init_com_thread() {
  COM_INITIALIZED.with(|init| {
    if !*init.borrow() {
      unsafe {
        CoInitializeEx(std::ptr::null(), COINIT_APARTMENTTHREADED as u32);
      }
      *init.borrow_mut() = true;
    }
  });
}

#[allow(dead_code)]
pub fn uninit_com_thread() {
  COM_INITIALIZED.with(|init| {
    if *init.borrow() {
      unsafe {
        CoUninitialize();
      }
      *init.borrow_mut() = false;
    }
  });
}
