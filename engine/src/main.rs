mod border;
mod config;
mod interactions;
mod ipc;
mod tiling;
mod window;

use std::{
  collections::HashMap,
  sync::{
    atomic::{AtomicUsize, Ordering},
    Arc,
  },
  time::{Duration, Instant},
};

use config::{load_config, EngineConfig};
use ipc::{EngineStatePayload, IpcRequest, IpcServer};
use tiling::{LayoutEngine, ScrollWorkspace};
use tokio::sync::{mpsc, Mutex};
use tracing::{info, warn};
use window::{
  get_monitor_rect_for_window, monitor_for_window, WindowManager,
};
use windows_sys::Win32::UI::WindowsAndMessaging::{
  GetForegroundWindow, SetForegroundWindow,
};

#[derive(Clone, Copy)]
struct WindowAnimation {
  from: tiling::Rect,
  to: tiling::Rect,
  started: Instant,
}

#[tokio::main]
async fn main() -> anyhow::Result<()> {
  tracing_subscriber::fmt().with_env_filter("info").init();

  info!("Initializing Monolith.Engine Windows 11 Native Tiling Core...");

  let mut raw_config = load_config();
  // Monolith is a scrolling window manager; normalize older configs on
  // load.
  raw_config.layout_mode = "Scrolling".to_string();
  let config = Arc::new(Mutex::new(raw_config));
  let active_workspace = Arc::new(AtomicUsize::new(1));

  let (ipc_server, state_tx, _) = IpcServer::new();
  let (action_tx, mut action_rx) = mpsc::channel::<IpcRequest>(64);
  let (move_size_tx, mut move_size_rx) =
    mpsc::unbounded_channel::<(isize, bool, tiling::Rect, i32, i32)>();
  let (pointer_tx, mut pointer_rx) =
    mpsc::unbounded_channel::<interactions::PointerInput>();
  interactions::start_move_size_listener(move_size_tx, pointer_tx);

  // Spawn IPC server
  let server_config = config.clone();
  let server_ws = active_workspace.clone();
  tokio::spawn(async move {
    if let Err(e) =
      ipc_server.run(server_config, server_ws, action_tx).await
    {
      warn!("IPC Server stopped: {}", e);
    }
  });

  // Window management loop
  let mut wm = WindowManager::new();
  // Future release: enable the focused-window border after visual/input
  // QA. let mut focus_border = border::FocusBorderOverlay::new();
  let mut layout_engine = LayoutEngine::new();
  let mut last_scan = Instant::now() - Duration::from_secs(1);
  // Keep a single config snapshot between scans instead of cloning
  // strings, rules, and vectors on every animation frame.
  let mut current_cfg = config.lock().await.clone();
  let mut last_targets: HashMap<isize, tiling::Rect> = HashMap::new();
  let mut animations: HashMap<isize, WindowAnimation> = HashMap::new();
  let mut workspace_transition_out =
    std::collections::HashSet::<isize>::new();
  let mut user_dragging = std::collections::HashSet::<isize>::new();
  let mut drag_start_rects: HashMap<isize, tiling::Rect> = HashMap::new();
  // Monolith workspaces are application-managed and do not switch or move
  // native Windows virtual desktops. Workspace assignments live on HWNDs
  // so they survive engine restarts while the windows remain open.
  let mut window_workspaces: HashMap<isize, usize> = HashMap::new();
  let mut last_focused_by_workspace: HashMap<usize, isize> =
    HashMap::new();
  let mut scroll_workspaces: HashMap<(isize, usize), ScrollWorkspace> =
    HashMap::new();

  info!("Monolith.Engine running. Listening for layout triggers and IPC commands...");

  let frame_tick =
    tokio::time::sleep(animation_frame_period(&current_cfg));
  tokio::pin!(frame_tick);

  loop {
    tokio::select! {
        _ = &mut frame_tick => {
            frame_tick.as_mut().reset(
                tokio::time::Instant::now() + animation_frame_period(&current_cfg));
            // Discovery does process/window inspection, so keep it off the 60 Hz
            // animation path. Layout and interpolation still update every frame.
            if last_scan.elapsed() >= Duration::from_millis(200) {
                let next_config = config.lock().await.clone();
                current_cfg = next_config;
                wm.scan_windows(&current_cfg);
                // Sync layout parameters when settings are refreshed.
                layout_engine.layout_mode.clone_from(&current_cfg.layout_mode);
                layout_engine.outer_top = current_cfg.outer_gaps_top;
                layout_engine.outer_bottom = current_cfg.outer_gaps_bottom;
                layout_engine.outer_left = current_cfg.outer_gaps_left;
                layout_engine.outer_right = current_cfg.outer_gaps_right;
                layout_engine.inner_gap = current_cfg.inner_gaps;
                layout_engine.min_width = current_cfg.min_width;
                layout_engine.min_height = current_cfg.min_height;
                last_scan = Instant::now();

                let alive: std::collections::HashSet<_> =
                    wm.managed_handles.iter().copied().collect();
                window_workspaces.retain(|hwnd, _| alive.contains(hwnd));
                last_focused_by_workspace.retain(|_, hwnd| alive.contains(hwnd));
                let active = active_workspace.load(Ordering::SeqCst).clamp(1, 9);
                for managed in &wm.windows {
                    let hwnd = managed.hwnd as isize;
                    if let Some(workspace) = window::window_workspace(managed.hwnd) {
                        window_workspaces.insert(hwnd, workspace.clamp(1, 9));
                    } else if managed.is_visible && !managed.is_minimized {
                        window::assign_window_workspace(managed.hwnd, active);
                        window_workspaces.insert(hwnd, active);
                    }
                }

                // Activating a taskbar button for a window on another
                // Monolith workspace switches back to that workspace.
                let foreground = unsafe { GetForegroundWindow() };
                if let Some(workspace) = window_workspaces
                    .get(&(foreground as isize))
                    .copied()
                    .filter(|workspace| *workspace != active)
                    // Ignore the outgoing foreground window while its slide
                    // animation is still in progress. Otherwise an empty
                    // destination workspace is immediately switched back.
                    .filter(|_| !workspace_transition_out.contains(&(foreground as isize)))
                    .filter(|_| window::is_active_workspace_window(foreground))
                {
                    active_workspace.store(workspace, Ordering::SeqCst);
                }

                let active = active_workspace.load(Ordering::SeqCst);
                for managed in &wm.windows {
                    let hwnd = managed.hwnd as isize;
                    if window_workspaces.get(&hwnd).copied().unwrap_or(active) == active {
                        window::show_from_workspace(managed.hwnd);
                    } else {
                        window::hide_for_workspace(managed.hwnd);
                    }
                }
            }

            // Filter windows for current active workspace
            let active = active_workspace.load(Ordering::SeqCst);
            let mut windows_by_workspace: HashMap<(isize, usize), Vec<_>> = HashMap::new();
            let ordered_windows: Vec<_> = wm.windows
                .iter()
                .filter(|w| !w.is_floating && w.is_visible && !w.is_minimized)
                .filter(|w| window_workspaces.get(&(w.hwnd as isize)).copied().unwrap_or(active) == active)
                .map(|w| w.hwnd)
                .collect();
            for hwnd in ordered_windows {
                let workspace = window_workspaces.get(&(hwnd as isize)).copied().unwrap_or(active);
                windows_by_workspace
                    .entry((monitor_for_window(hwnd), workspace))
                    .or_default()
                    .push(hwnd);
            }

            let mut targets = HashMap::new();
            let foreground = unsafe { GetForegroundWindow() };
            if let Some(workspace) = window_workspaces.get(&(foreground as isize)) {
                last_focused_by_workspace.insert(*workspace, foreground as isize);
            }
            // Future release: uncomment to show the adjustable Komorebi-style
            // outline only around a visible managed foreground window.
            // let focused_is_managed = wm.windows.iter().any(|window| window.hwnd == foreground && window.is_visible && !window.is_minimized);
            // focus_border.update(if focused_is_managed { foreground } else { std::ptr::null_mut() }, &current_cfg.active_border_color, current_cfg.border_thickness);
            for ((monitor, workspace_id), windows) in &windows_by_workspace {
                if let Some(hwnd) = windows.first() {
                    let screen = get_monitor_rect_for_window(*hwnd);
                    // PaperWM-style columns use the available viewport width by
                    // default. This makes the focused column honor both side gaps;
                    // users can still resize a column explicitly.
                    let initial_width = (screen.width - current_cfg.outer_gaps_left - current_cfg.outer_gaps_right)
                        .max(current_cfg.min_width).min(screen.width.max(1));
                    let workspace = scroll_workspaces.entry((*monitor, *workspace_id)).or_default();
                    workspace.sync(windows, initial_width);
                    let rects = workspace.compute_layout(screen, &layout_engine, foreground);
                    for (window, rect) in rects {
                        targets.insert(window as isize, rect);
                    }
                }
            }

            if current_cfg.enable_animations {
                let now = Instant::now();
                let live: std::collections::HashSet<_> = targets.keys().copied().collect();
                last_targets.retain(|hwnd, _| live.contains(hwnd) || workspace_transition_out.contains(hwnd));
                animations.retain(|hwnd, _| live.contains(hwnd) || workspace_transition_out.contains(hwnd));

                for (&hwnd, &target) in &targets {
                    if user_dragging.contains(&hwnd) { continue; }
                    if last_targets.get(&hwnd).is_some_and(|previous| *previous == target) {
                        continue;
                    }
                    if let Some(from) = wm.window_rect(hwnd as _) {
                        animations.insert(hwnd, WindowAnimation { from, to: target, started: now });
                    }
                    last_targets.insert(hwnd, target);
                }

                let duration = Duration::from_millis(current_cfg.reflow_duration.clamp(80, 400) as u64);
                let mut frame = Vec::with_capacity(animations.len());
                let mut finished = Vec::new();
                for (&hwnd, animation) in &animations {
                    if user_dragging.contains(&hwnd) { continue; }
                    let progress = (now.duration_since(animation.started).as_secs_f64() / duration.as_secs_f64()).clamp(0.0, 1.0);
                    // Quintic smoothstep starts and ends at zero velocity for a softer scroll.
                    let eased = progress * progress * progress * (progress * (progress * 6.0 - 15.0) + 10.0);
                    let mix = |a: i32, b: i32| a + ((b - a) as f64 * eased).round() as i32;
                    frame.push((hwnd as _, tiling::Rect::new(
                        mix(animation.from.x, animation.to.x),
                        mix(animation.from.y, animation.to.y),
                        mix(animation.from.width, animation.to.width).max(1),
                        mix(animation.from.height, animation.to.height).max(1),
                    )));
                    if progress >= 1.0 { finished.push(hwnd); }
                }
                let mut hide_after_slide = Vec::new();
                for hwnd in finished {
                    animations.remove(&hwnd);
                    if workspace_transition_out.remove(&hwnd) {
                        hide_after_slide.push(hwnd);
                        last_targets.remove(&hwnd);
                    }
                }
                wm.apply_layout(&frame);
                for hwnd in hide_after_slide {
                    window::hide_for_workspace(hwnd as _);
                }
            } else {
                for hwnd in workspace_transition_out.drain() {
                    window::hide_for_workspace(hwnd as _);
                }
                animations.clear();
                last_targets = targets;
                let rects: Vec<_> = last_targets.iter().filter(|(hwnd, _)| !user_dragging.contains(hwnd)).map(|(&hwnd, &rect)| (hwnd as _, rect)).collect();
                wm.apply_layout(&rects);
            }
        }

        Some((hwnd, is_start, event_rect, cursor_x, cursor_y)) = move_size_rx.recv() => {
            if is_start {
                user_dragging.insert(hwnd);
                drag_start_rects.insert(hwnd, event_rect);
                animations.remove(&hwnd);
            } else if user_dragging.remove(&hwnd) {
                // Preserve native edge-resizes in the tiled geometry. A move
                // without a size change remains a drag and snaps back.
                if let Some(before) = drag_start_rects.remove(&hwnd) {
                    let after = event_rect;
                    let width_delta = after.width - before.width;
                    let height_delta = after.height - before.height;
                    let cfg = config.lock().await.clone();
                    let key = workspace_key(&window_workspaces, active_workspace.load(Ordering::SeqCst), hwnd as _);
                    let monitor = get_monitor_rect_for_window(hwnd as _);
                    let mut dropped = false;
                    if width_delta.abs() <= 2 && height_delta.abs() <= 2
                        && ((after.x - before.x).abs() > 24 || (after.y - before.y).abs() > 24)
                    {
                        if let Some(workspace) = scroll_workspaces.get_mut(&key) {
                            dropped = workspace.drop_into_column(hwnd as _, cursor_x, cursor_y, monitor, &layout_engine, hwnd as _);
                            if dropped { info!("Window {:?} dropped into a vertical column", hwnd); }
                        }
                    }
                    if !dropped && (width_delta.abs() > 2 || height_delta.abs() > 2) {
                        if let Some(workspace) = scroll_workspaces.get_mut(&key) {
                            workspace.adopt_user_resize(hwnd as _, width_delta, height_delta, monitor, cfg.min_width, cfg.min_height);
                        }
                    }
                }
                // Animate from the released position to its updated tiled slot.
                last_targets.remove(&hwnd);
                animations.remove(&hwnd);
            }
        }

        Some(input) = pointer_rx.recv() => {
            match input {
                interactions::PointerInput::Wheel { hwnd, direction } => {
                    let foreground = if hwnd == 0 { unsafe { GetForegroundWindow() } } else { hwnd as _ };
                    let key = workspace_key(&window_workspaces, active_workspace.load(Ordering::SeqCst), foreground);
                    let direction_name = if direction > 0 { "right" } else { "left" };
                    if let Some(target) = scroll_workspaces.get(&key).and_then(|workspace| workspace.focus(foreground, direction_name)) {
                        unsafe { SetForegroundWindow(target); }
                    }
                }
                interactions::PointerInput::EdgeClick { hwnd, x, y } => {
                    if !current_cfg.edge_navigation_enabled {
                        continue;
                    }
                    if let Some((monitor, bounds)) = window::monitor_at_point(x, y) {
                        // A small physical-screen hit zone keeps normal clicks
                        // untouched while making edge navigation work on every
                        // monitor, including when an app window has focus.
                        const EDGE_ZONE: i32 = 40;
                        let direction = if x < bounds.x + EDGE_ZONE {
                            Some("left")
                        } else if x >= bounds.x + bounds.width - EDGE_ZONE {
                            Some("right")
                        } else {
                            None
                        };
                        if let Some(direction) = direction {
                            let clicked_window = if hwnd == 0 {
                                unsafe { GetForegroundWindow() as isize }
                            } else {
                                hwnd
                            };
                            let key = (monitor, active_workspace.load(Ordering::SeqCst));
                            if let Some(workspace) = scroll_workspaces.get(&key) {
                                // Use the foreground captured synchronously by
                                // the mouse hook. The frame loop can update its
                                // focus cache before this queued event is read.
                                if let Some(target) = workspace.focus(clicked_window as _, direction) {
                                    unsafe {
                                        windows_sys::Win32::UI::WindowsAndMessaging::BringWindowToTop(target);
                                        SetForegroundWindow(target);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        Some(req) = action_rx.recv() => {
            info!("Received IPC command: {} from {}", req.command, req.source);

            match req.command.as_str() {
                "SET_STATE" => {
                    if let Ok(new_cfg) = serde_json::from_value::<EngineConfig>(req.args) {
                        let mut cfg = config.lock().await;
                        let mut new_cfg = new_cfg;
                        new_cfg.layout_mode = "Scrolling".to_string();
                        *cfg = new_cfg;
                        // The settings app owns the shared JSON document and may contain
                        // UI-only fields (hotkeys, launcher state, etc). Persisting this
                        // narrower engine model here would erase those fields.
                        info!("Updated engine state from the shared settings document.");
                    }
                }
                "RELOAD_CONFIG" => {
                    let reloaded = load_config();
                    let mut cfg = config.lock().await;
                    *cfg = reloaded;
                    info!("Reloaded config from disk.");
                }
                "FOCUS_WORKSPACE" | "FOCUS_WORKSPACE_DELTA" => {
                    let previous = active_workspace.load(Ordering::SeqCst).clamp(1, 9);
                    let target = if req.command == "FOCUS_WORKSPACE" {
                        req.args.get("workspace").and_then(|value| value.as_u64()).map(|value| value as usize)
                    } else {
                        req.args.get("delta").and_then(|value| value.as_i64()).map(|delta| {
                            let next = previous as i64 + delta.signum();
                            if current_cfg.wrap_around {
                                if next < 1 { 9 } else if next > 9 { 1 } else { next as usize }
                            } else {
                                next.clamp(1, 9) as usize
                            }
                        })
                    }.map(|workspace| workspace.clamp(1, 9));

                    if let Some(target) = target.filter(|target| *target != previous) {
                        active_workspace.store(target, Ordering::SeqCst);
                        // If a switch is interrupted, finish hiding the
                        // outgoing windows before starting the next slide.
                        for hwnd in workspace_transition_out.drain() {
                            animations.remove(&hwnd);
                            last_targets.remove(&hwnd);
                            window::hide_for_workspace(hwnd as _);
                        }
                        let animate = current_cfg.enable_animations
                            && !current_cfg.animation_mode.eq_ignore_ascii_case("None");
                        let started = Instant::now();
                        for managed in &wm.windows {
                            let hwnd = managed.hwnd as isize;
                            let assigned = window_workspaces.get(&hwnd).copied().unwrap_or(previous);
                            if assigned == previous {
                                if animate && managed.is_visible && !managed.is_minimized {
                                    if let Some(from) = wm.window_rect(managed.hwnd) {
                                        let monitor = get_monitor_rect_for_window(managed.hwnd);
                                        let offset = if target > previous { -monitor.height } else { monitor.height };
                                        animations.insert(hwnd, WindowAnimation {
                                            from,
                                            to: tiling::Rect::new(from.x, from.y + offset, from.width, from.height),
                                            started,
                                        });
                                        workspace_transition_out.insert(hwnd);
                                    } else {
                                        window::hide_for_workspace(managed.hwnd);
                                    }
                                } else {
                                    window::hide_for_workspace(managed.hwnd);
                                }
                            } else if assigned == target {
                                let was_workspace_hidden =
                                    window::is_hidden_for_workspace(managed.hwnd);
                                if animate && was_workspace_hidden {
                                    if let Some(from) = wm.window_rect(managed.hwnd) {
                                        let monitor = get_monitor_rect_for_window(managed.hwnd);
                                        let offset = if target > previous { monitor.height } else { -monitor.height };
                                        let start = tiling::Rect::new(from.x, from.y + offset, from.width, from.height);
                                        wm.apply_layout(&[(managed.hwnd, start)]);
                                        window::show_from_workspace(managed.hwnd);
                                        last_targets.remove(&hwnd);
                                        animations.remove(&hwnd);
                                    } else {
                                        window::show_from_workspace(managed.hwnd);
                                    }
                                } else {
                                    window::show_from_workspace(managed.hwnd);
                                }
                            } else {
                                window::hide_for_workspace(managed.hwnd);
                            }
                        }
                        let target_window = last_focused_by_workspace.get(&target).copied()
                            .filter(|hwnd| wm.managed_handles.contains(hwnd))
                            .or_else(|| window_workspaces.iter().find_map(|(hwnd, workspace)| (*workspace == target).then_some(*hwnd)));
                        if let Some(hwnd) = target_window {
                            window::show_from_workspace(hwnd as _);
                            unsafe { SetForegroundWindow(hwnd as _); }
                        } else {
                            // Clear the outgoing app's foreground ownership so
                            // new windows join the now-active empty workspace.
                            window::focus_desktop();
                        }
                        wm.scan_windows(&current_cfg);
                        last_scan = Instant::now();
                        info!("Switched Monolith workspace {} -> {}", previous, target);
                    }
                }
                "FOCUS_DIRECTION" => {
                    let direction = req.args.get("direction").and_then(|v| v.as_str()).unwrap_or("");
                    let foreground = unsafe { GetForegroundWindow() };
                    let key = workspace_key(&window_workspaces, active_workspace.load(Ordering::SeqCst), foreground);
                    if let Some(target) = scroll_workspaces.get(&key).and_then(|workspace| workspace.focus(foreground, direction)) {
                        unsafe { SetForegroundWindow(target); }
                    }
                }
                "MOVE_WINDOW_DIRECTION" => {
                    let direction = req.args.get("direction").and_then(|v| v.as_str()).unwrap_or("");
                    let foreground = unsafe { GetForegroundWindow() };
                    let target = req.args.get("hwnd").and_then(|value| value.as_i64())
                        .map(|value| value as _).unwrap_or(foreground);
                    let key = workspace_key(&window_workspaces, active_workspace.load(Ordering::SeqCst), target);
                    if let Some(workspace) = scroll_workspaces.get_mut(&key) {
                        workspace.move_window(target, direction);
                    }
                }
                "TOGGLE_FLOATING" => {
                    let target_hwnd = req.args.get("hwnd").and_then(|v| v.as_i64()).map(|v| v as isize)
                        .unwrap_or_else(|| unsafe { GetForegroundWindow() } as isize);
                    let hwnd_val = target_hwnd;
                    if hwnd_val != 0 {
                        if wm.floating_windows.contains(&hwnd_val) {
                            wm.floating_windows.remove(&hwnd_val);
                            info!("Window {:?} switched to tiled mode.", hwnd_val);
                        } else {
                            wm.floating_windows.insert(hwnd_val);
                            info!("Window {:?} switched to floating mode.", hwnd_val);
                        }
                    }
                }
                "TOGGLE_VERTICAL_TILE" => {
                    let foreground = unsafe { GetForegroundWindow() };
                    let target = req.args.get("hwnd").and_then(|value| value.as_i64())
                        .map(|value| value as _).unwrap_or(foreground);
                    let key = workspace_key(&window_workspaces, active_workspace.load(Ordering::SeqCst), target);
                    if let Some(workspace) = scroll_workspaces.get_mut(&key) {
                        workspace.toggle_vertical(target);
                    }
                }
                "TOGGLE_MONOCLE" => {
                    let foreground = unsafe { GetForegroundWindow() };
                    let target = req.args.get("hwnd").and_then(|value| value.as_i64())
                        .map(|value| value as _).unwrap_or(foreground);
                    let key = workspace_key(&window_workspaces, active_workspace.load(Ordering::SeqCst), target);
                    if let Some(workspace) = scroll_workspaces.get_mut(&key) {
                        workspace.toggle_monocle(target);
                    }
                }
                "RESIZE_WINDOW" => {
                    let resize_cfg = { config.lock().await.clone() };
                    let foreground = unsafe { GetForegroundWindow() };
                    let target = req.args.get("hwnd").and_then(|value| value.as_i64())
                        .map(|value| value as _).unwrap_or(foreground);
                    let axis = req.args.get("axis").and_then(|v| v.as_str()).unwrap_or("");
                    let direction = req.args.get("direction").and_then(|v| v.as_i64()).unwrap_or(0).signum() as i32;
                    if direction == 0 { continue; }
                    let screen = get_monitor_rect_for_window(target);
                    let step = (if axis == "width" { screen.width } else { screen.height }) * resize_cfg.resize_step_percentage.clamp(1, 25) / 100;
                    let key = workspace_key(&window_workspaces, active_workspace.load(Ordering::SeqCst), target);
                    if let Some(workspace) = scroll_workspaces.get_mut(&key) {
                        workspace.resize(target, axis, direction, step.max(8), screen, resize_cfg.min_width, resize_cfg.min_height);
                    }
                }
                "MOVE_WINDOW" | "MOVE_WINDOW_WORKSPACE_DELTA" => {
                    let foreground = unsafe { GetForegroundWindow() };
                    let hwnd = req.args.get("hwnd").and_then(|value| value.as_i64()).map(|value| value as isize).unwrap_or(foreground as isize);
                    let current = window_workspaces.get(&hwnd).copied().unwrap_or(active_workspace.load(Ordering::SeqCst));
                    let target = if req.command == "MOVE_WINDOW" {
                        req.args.get("workspace").and_then(|value| value.as_u64()).map(|value| value as usize)
                    } else {
                        req.args.get("delta").and_then(|value| value.as_i64()).map(|delta| {
                            let next = current as i64 + delta.signum();
                            if current_cfg.wrap_around {
                                if next < 1 { 9 } else if next > 9 { 1 } else { next as usize }
                            } else {
                                next.clamp(1, 9) as usize
                            }
                        })
                    }.map(|workspace| workspace.clamp(1, 9));
                    if let Some(target) = target.filter(|target| *target != current) {
                        window_workspaces.insert(hwnd, target);
                        window::assign_window_workspace(hwnd as _, target);
                        if target != active_workspace.load(Ordering::SeqCst) {
                            window::hide_for_workspace(hwnd as _);
                        }
                        info!("Moved window {:?} to Monolith workspace {}", hwnd, target);
                    }
                }
                _ => {}
            }

            // Broadcast updated state
            let cfg = config.lock().await;
            let payload = EngineStatePayload {
                active_workspace: active_workspace.load(Ordering::SeqCst),
                total_workspaces: 9,
                layout_mode: cfg.layout_mode.clone(),
                outer_gaps: (cfg.outer_gaps_top, cfg.outer_gaps_bottom, cfg.outer_gaps_left, cfg.outer_gaps_right),
                inner_gaps: cfg.inner_gaps,
                windows: Vec::new(),
            };
            if let Ok(json) = serde_json::to_string(&payload) {
                let _ = state_tx.send(json);
            }
        }

        _ = tokio::signal::ctrl_c() => {
            info!("Received shutdown signal. Exiting Monolith.Engine...");
            break;
        }
    }
  }

  Ok(())
}

fn animation_frame_period(config: &EngineConfig) -> Duration {
  if !config.enable_animations {
    return Duration::from_millis(200);
  }
  match config.animation_mode.to_ascii_lowercase().as_str() {
    "smooth" => Duration::from_nanos(1_000_000_000 / 120),
    "minimal" => Duration::from_nanos(1_000_000_000 / 60),
    _ => Duration::from_millis(200),
  }
}

fn workspace_key(
  window_workspaces: &HashMap<isize, usize>,
  active_workspace: usize,
  hwnd: windows_sys::Win32::Foundation::HWND,
) -> (isize, usize) {
  (
    monitor_for_window(hwnd),
    window_workspaces
      .get(&(hwnd as isize))
      .copied()
      .unwrap_or(active_workspace),
  )
}
