use std::collections::HashMap;

use serde::{Deserialize, Serialize};
use windows_sys::Win32::Foundation::HWND;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct Rect {
  pub x: i32,
  pub y: i32,
  pub width: i32,
  pub height: i32,
}

impl Rect {
  pub fn new(x: i32, y: i32, width: i32, height: i32) -> Self {
    Self {
      x,
      y,
      width,
      height,
    }
  }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct WindowNode {
  pub hwnd: isize,
  pub title: String,
  pub process_name: String,
  pub is_floating: bool,
  pub workspace: usize,
  pub rect: Rect,
}

#[derive(Debug, Clone)]
pub struct LayoutEngine {
  pub layout_mode: String,
  pub outer_top: i32,
  pub outer_bottom: i32,
  pub outer_left: i32,
  pub outer_right: i32,
  pub inner_gap: i32,
  pub min_width: i32,
  pub min_height: i32,
  pub vertical_pairs: HashMap<isize, isize>,
  pub window_sizes: HashMap<isize, (i32, i32)>,
}

/// Stateful scroll-first layout: each column owns a horizontal position
/// and width, and contains zero or more vertically stacked windows with
/// weights.
#[derive(Debug, Clone, Default)]
pub struct ScrollWorkspace {
  columns: Vec<ScrollColumn>,
  monocle: Option<isize>,
  last_focused: Option<isize>,
  /// Width selected by the user most recently in this desktop/monitor
  /// row. New columns inherit it instead of resetting to full monitor
  /// width.
  last_resized_width: Option<i32>,
}

#[derive(Debug, Clone)]
struct ScrollColumn {
  windows: Vec<isize>,
  width: i32,
  height_weights: Vec<f64>,
}

impl ScrollWorkspace {
  /// Keep existing column order and memberships, drop windows that left
  /// this monitor/workspace view, and add newly discovered windows as
  /// columns.
  pub fn sync(
    &mut self,
    windows: &[HWND],
    initial_width: i32,
  ) -> Vec<isize> {
    for column in &mut self.columns {
      let mut index = 0;
      while index < column.windows.len() {
        if windows
          .iter()
          .any(|hwnd| *hwnd as isize == column.windows[index])
        {
          index += 1;
        } else {
          column.windows.remove(index);
          if index < column.height_weights.len() {
            column.height_weights.remove(index);
          }
        }
      }
      normalize_weights(&mut column.height_weights);
    }
    self.columns.retain(|column| !column.windows.is_empty());
    let contains = |id: isize, columns: &[ScrollColumn]| {
      columns.iter().any(|column| column.windows.contains(&id))
    };
    if self
      .last_focused
      .is_some_and(|id| !contains(id, &self.columns))
    {
      self.last_focused = None;
    }
    let mut added = Vec::new();
    for hwnd in windows {
      let id = *hwnd as isize;
      let already_known = self
        .columns
        .iter()
        .any(|column| column.windows.contains(&id));
      if !already_known {
        added.push(id);
        self.columns.push(ScrollColumn {
          windows: vec![id],
          width: self.last_resized_width.unwrap_or(initial_width).max(100),
          height_weights: vec![1.0],
        });
      }
    }
    added
  }

  pub fn compute_layout(
    &mut self,
    monitor: Rect,
    layout: &LayoutEngine,
    focused: HWND,
  ) -> Vec<(HWND, Rect)> {
    if self.columns.is_empty() {
      return Vec::new();
    }
    let left = layout.outer_left.clamp(0, (monitor.width - 1).max(0));
    let right = layout
      .outer_right
      .clamp(0, (monitor.width - left - 1).max(0));
    let top = layout.outer_top.clamp(0, (monitor.height - 1).max(0));
    let bottom = layout
      .outer_bottom
      .clamp(0, (monitor.height - top - 1).max(0));
    let bounds = Rect::new(
      monitor.x + left,
      monitor.y + top,
      (monitor.width - left - right).max(1),
      (monitor.height - top - bottom).max(1),
    );
    if let Some(monocle) = self.monocle {
      return vec![(monocle as HWND, bounds)];
    }
    let requested_focus = focused as isize;
    let requested_is_managed = self
      .columns
      .iter()
      .any(|column| column.windows.contains(&requested_focus));
    let focused_id = if requested_is_managed {
      requested_focus
    } else {
      self
        .last_focused
        .filter(|id| {
          self
            .columns
            .iter()
            .any(|column| column.windows.contains(id))
        })
        .or_else(|| {
          self
            .columns
            .first()
            .and_then(|column| column.windows.first())
            .copied()
        })
        .unwrap_or(requested_focus)
    };
    if requested_is_managed {
      self.last_focused = Some(focused_id);
    }
    let focused_column = self
      .columns
      .iter()
      .position(|column| column.windows.contains(&focused_id))
      .unwrap_or(0);
    let widths: Vec<i32> = self
      .columns
      .iter()
      .map(|column| {
        column
          .width
          .clamp(layout.min_width.min(bounds.width).max(1), bounds.width)
      })
      .collect();
    let mut x_positions = vec![bounds.x; self.columns.len()];
    let focused_width = widths[focused_column];
    // Keep the focused column in the center of the viewport. The scrolling
    // workspace has one predictable placement behavior; it never anchors to
    // either screen edge based on a selectable mode.
    x_positions[focused_column] = bounds.x + (bounds.width - focused_width) / 2;
    for index in (0..focused_column).rev() {
      x_positions[index] =
        x_positions[index + 1] - widths[index] - layout.inner_gap.max(0);
    }
    for index in focused_column + 1..self.columns.len() {
      x_positions[index] = x_positions[index - 1]
        + widths[index - 1]
        + layout.inner_gap.max(0);
    }

    let mut result = Vec::new();
    for (column_index, column) in self.columns.iter().enumerate() {
      let count = column.windows.len();
      let gap = layout.inner_gap.clamp(
        0,
        (bounds.height - count as i32).max(0)
          / (count.saturating_sub(1) as i32).max(1),
      );
      let usable_height = (bounds.height
        - gap * count.saturating_sub(1) as i32)
        .max(count as i32);
      let mut weights = column.height_weights.clone();
      if weights.len() != count {
        weights = vec![1.0 / count as f64; count];
      }
      let total: f64 = weights.iter().sum();
      if total <= 0.0 {
        weights.fill(1.0 / count as f64);
      } else {
        for weight in &mut weights {
          *weight /= total;
        }
      }
      let mut y = bounds.y;
      for (index, hwnd) in column.windows.iter().enumerate() {
        let height = if index + 1 == count {
          bounds.y + bounds.height - y
        } else {
          (usable_height as f64 * weights[index]).round() as i32
        };
        result.push((
          *hwnd as HWND,
          Rect::new(
            x_positions[column_index],
            y,
            widths[column_index],
            height.max(1),
          ),
        ));
        y += height + gap;
      }
    }
    result
  }

  /// Focus an adjacent window in the horizontal scroller or vertical stack.
  /// When wrapping is enabled, navigation from either end continues at the
  /// opposite end of that axis.
  pub fn focus(
    &self,
    focused: HWND,
    direction: &str,
    wrap_around: bool,
  ) -> Option<HWND> {
    let id = focused as isize;
    let position = |id: isize| self.columns.iter().enumerate().find_map(|(ci, column)| {
        column
          .windows
          .iter()
          .position(|window| *window == id)
          .map(|wi| (ci, wi))
      });
    // Screen-edge clicks can land on the desktop or shell, neither of which
    // is a managed app. Continue navigation from the last tiled focus in this
    // workspace instead of silently dropping the click.
    let (column_index, window_index) = position(id)
      .or_else(|| self.last_focused.and_then(position))
      .or_else(|| {
        self.columns
          .first()
          .and_then(|column| (!column.windows.is_empty()).then_some((0, 0)))
      })?;
    let column_count = self.columns.len();
    let current_column = &self.columns[column_index];
    let target = match direction {
      "left" if column_index > 0 => {
        let target_column = &self.columns[column_index - 1];
        target_column.windows.get(window_index.min(target_column.windows.len() - 1))
      }
      "left" if wrap_around && column_count > 1 => {
        let target_column = &self.columns[column_count - 1];
        target_column.windows.get(window_index.min(target_column.windows.len() - 1))
      }
      "right" if column_index + 1 < column_count => {
        let target_column = &self.columns[column_index + 1];
        target_column.windows.get(window_index.min(target_column.windows.len() - 1))
      }
      "right" if wrap_around && column_count > 1 => {
        let target_column = &self.columns[0];
        target_column.windows.get(window_index.min(target_column.windows.len() - 1))
      }
      "up" if window_index > 0 => current_column.windows.get(window_index - 1),
      "up" if wrap_around && current_column.windows.len() > 1 => current_column.windows.last(),
      "down" if window_index + 1 < current_column.windows.len() => current_column.windows.get(window_index + 1),
      "down" if wrap_around && current_column.windows.len() > 1 => current_column.windows.first(),
      _ => None,
    }?;
    Some(*target as HWND)
  }

  /// Move the focused window through the column/stack order without
  /// changing focus. Horizontal movement reorders columns (or detaches a
  /// stacked window into a neighboring column); vertical movement reorders
  /// windows within their current column.
  pub fn move_window(&mut self, focused: HWND, direction: &str) {
    let id = focused as isize;
    let Some(column_index) = self.columns.iter().position(|column| column.windows.contains(&id)) else {
      return;
    };
    let window_index = self.columns[column_index].windows.iter().position(|window| *window == id).unwrap();
    match direction {
      "up" if window_index > 0 => {
        self.columns[column_index].windows.swap(window_index, window_index - 1);
        self.columns[column_index].height_weights.swap(window_index, window_index - 1);
      }
      "down" if window_index + 1 < self.columns[column_index].windows.len() => {
        self.columns[column_index].windows.swap(window_index, window_index + 1);
        self.columns[column_index].height_weights.swap(window_index, window_index + 1);
      }
      "left" if column_index > 0 => {
        if self.columns[column_index].windows.len() == 1 {
          self.columns.swap(column_index, column_index - 1);
        } else {
          let width = self.columns[column_index].width;
          self.columns[column_index].windows.remove(window_index);
          self.columns[column_index].height_weights.remove(window_index);
          normalize_weights(&mut self.columns[column_index].height_weights);
          self.columns.insert(column_index - 1, ScrollColumn { windows: vec![id], width, height_weights: vec![1.0] });
        }
      }
      "right" if column_index + 1 < self.columns.len() => {
        if self.columns[column_index].windows.len() == 1 {
          self.columns.swap(column_index, column_index + 1);
        } else {
          let width = self.columns[column_index].width;
          self.columns[column_index].windows.remove(window_index);
          self.columns[column_index].height_weights.remove(window_index);
          normalize_weights(&mut self.columns[column_index].height_weights);
          self.columns.insert(column_index + 1, ScrollColumn { windows: vec![id], width, height_weights: vec![1.0] });
        }
      }
      _ => {}
    }
    self.last_focused = Some(id);
  }

  /// Toggle vertical stacking by consuming the next column's focused
  /// window, or expelling the focused window back into its own
  /// horizontal column.
  pub fn toggle_vertical(&mut self, focused: HWND) {
    let id = focused as isize;
    let Some(column_index) = self
      .columns
      .iter()
      .position(|column| column.windows.contains(&id))
    else {
      return;
    };
    if self.columns[column_index].windows.len() > 1 {
      let window_index = self.columns[column_index]
        .windows
        .iter()
        .position(|window| *window == id)
        .unwrap();
      let width = self.columns[column_index].width;
      self.columns[column_index].windows.remove(window_index);
      self.columns[column_index]
        .height_weights
        .remove(window_index);
      normalize_weights(&mut self.columns[column_index].height_weights);
      self.columns.insert(
        column_index + 1,
        ScrollColumn {
          windows: vec![id],
          width,
          height_weights: vec![1.0],
        },
      );
    } else {
      let neighbor_index = if column_index + 1 < self.columns.len() {
        Some(column_index + 1)
      } else {
        column_index.checked_sub(1)
      };
      if let Some(neighbor_index) = neighbor_index {
        let moving_index = if neighbor_index > column_index {
          neighbor_index
        } else {
          column_index
        };
        let moving = self.columns[moving_index].windows.remove(0);
        self.columns[moving_index].height_weights.remove(0);
        let target_index = if neighbor_index > column_index {
          column_index
        } else {
          neighbor_index
        };
        self.columns[target_index].windows.push(moving);
        self.columns[target_index].height_weights.push(1.0);
        normalize_weights(&mut self.columns[target_index].height_weights);
        if self.columns[moving_index].windows.is_empty() {
          self.columns.remove(moving_index);
        }
      }
    }
  }

  /// Drop a dragged tiled window onto another column to make it a vertical
  /// neighbor. The destination is chosen from the canonical layout
  /// rectangles.
  pub fn drop_into_column(
    &mut self,
    dragged: HWND,
    cursor_x: i32,
    cursor_y: i32,
    monitor: Rect,
    layout: &LayoutEngine,
    focused: HWND,
  ) -> bool {
    let dragged_id = dragged as isize;
    let canonical = self.compute_layout(monitor, layout, focused);
    let Some((target_id, target_rect)) = canonical
      .iter()
      .find(|(hwnd, rect)| {
        *hwnd as isize != dragged_id
          && cursor_x >= rect.x
          && cursor_x < rect.x + rect.width
          && cursor_y >= rect.y
          && cursor_y < rect.y + rect.height
      })
      .map(|(hwnd, rect)| (*hwnd as isize, *rect))
    else {
      return false;
    };
    let Some((source_column, source_index)) =
      self.columns.iter().enumerate().find_map(|(ci, column)| {
        column
          .windows
          .iter()
          .position(|window| *window == dragged_id)
          .map(|wi| (ci, wi))
      })
    else {
      return false;
    };
    let Some((target_column, target_index)) =
      self.columns.iter().enumerate().find_map(|(ci, column)| {
        column
          .windows
          .iter()
          .position(|window| *window == target_id)
          .map(|wi| (ci, wi))
      })
    else {
      return false;
    };

    if source_column == target_column {
      let column = &mut self.columns[source_column];
      let weight = column.height_weights.remove(source_index);
      column.windows.remove(source_index);
      let adjusted_target = if source_index < target_index {
        target_index - 1
      } else {
        target_index
      };
      let insert_after =
        cursor_y >= target_rect.y + target_rect.height / 2;
      let insert_at = (adjusted_target + usize::from(insert_after))
        .min(column.windows.len());
      column.windows.insert(insert_at, dragged_id);
      column.height_weights.insert(insert_at, weight);
      normalize_weights(&mut column.height_weights);
      return insert_at != source_index;
    }

    self.columns[source_column].windows.remove(source_index);
    self.columns[source_column]
      .height_weights
      .remove(source_index);
    normalize_weights(&mut self.columns[source_column].height_weights);
    if self.columns[source_column].windows.is_empty() {
      self.columns.remove(source_column);
    }
    let target_column = if source_column < target_column {
      target_column - 1
    } else {
      target_column
    };
    let column = &mut self.columns[target_column];
    let target_index =
      target_index.min(column.windows.len().saturating_sub(1));
    if column.height_weights.len() != column.windows.len() {
      column.height_weights =
        vec![1.0 / column.windows.len() as f64; column.windows.len()];
    }
    let old_weight = column.height_weights[target_index];
    let insert_after = cursor_y >= target_rect.y + target_rect.height / 2;
    let insert_at = target_index + usize::from(insert_after);
    column.height_weights[target_index] = old_weight / 2.0;
    column.height_weights.insert(insert_at, old_weight / 2.0);
    column.windows.insert(insert_at, dragged_id);
    normalize_weights(&mut column.height_weights);
    true
  }

  /// Detach a window from a vertical stack when it is dragged out through
  /// a side of its column. Its old width follows it into a new
  /// horizontal slot.
  pub fn detach_from_column(
    &mut self,
    dragged: HWND,
    cursor_x: i32,
    monitor: Rect,
    layout: &LayoutEngine,
    focused: HWND,
  ) -> bool {
    let id = dragged as isize;
    let Some((source_index, window_index)) =
      self.columns.iter().enumerate().find_map(|(ci, column)| {
        column
          .windows
          .iter()
          .position(|window| *window == id)
          .map(|wi| (ci, wi))
      })
    else {
      return false;
    };
    if self.columns[source_index].windows.len() < 2 {
      return false;
    }
    let rects = self.compute_layout(monitor, layout, focused);
    let Some(source_rect) = rects
      .iter()
      .find(|(hwnd, _)| *hwnd as isize == id)
      .map(|(_, rect)| *rect)
    else {
      return false;
    };
    if cursor_x >= source_rect.x
      && cursor_x < source_rect.x + source_rect.width
    {
      return false;
    }

    let column = &mut self.columns[source_index];
    let width = column.width;
    column.windows.remove(window_index);
    column.height_weights.remove(window_index);
    normalize_weights(&mut column.height_weights);
    let new_index = if cursor_x < source_rect.x {
      source_index
    } else {
      source_index + 1
    };
    self.columns.insert(
      new_index,
      ScrollColumn {
        windows: vec![id],
        width,
        height_weights: vec![1.0],
      },
    );
    true
  }

  pub fn toggle_monocle(&mut self, focused: HWND) {
    let id = focused as isize;
    if !self
      .columns
      .iter()
      .any(|column| column.windows.contains(&id))
    {
      return;
    }
    self.monocle = if self.monocle == Some(id) {
      None
    } else {
      Some(id)
    };
  }

  pub fn resize(
    &mut self,
    focused: HWND,
    axis: &str,
    direction: i32,
    step: i32,
    monitor: Rect,
    min_width: i32,
    min_height: i32,
  ) {
    let id = focused as isize;
    let Some((column_index, window_index)) =
      self.columns.iter().enumerate().find_map(|(ci, column)| {
        column
          .windows
          .iter()
          .position(|window| *window == id)
          .map(|wi| (ci, wi))
      })
    else {
      return;
    };
    match axis {
      "width" => {
        self.columns[column_index].width =
          (self.columns[column_index].width + step * direction).clamp(
            min_width.min(monitor.width).max(1),
            monitor.width.max(1),
          );
        self.last_resized_width = Some(self.columns[column_index].width);
      }
      "height" if self.columns[column_index].windows.len() > 1 => {
        let column = &mut self.columns[column_index];
        if column.height_weights.len() != column.windows.len() {
          column.height_weights =
            vec![1.0 / column.windows.len() as f64; column.windows.len()];
        }
        let delta =
          (step as f64 / monitor.height.max(1) as f64) * direction as f64;
        let min_weight = (min_height.max(1) as f64
          / monitor.height.max(1) as f64)
          .clamp(0.01, 0.49);
        let next = (column.height_weights[window_index] + delta)
          .clamp(min_weight, 1.0 - min_weight);
        column.height_weights[window_index] = next;
        let remainder = 1.0 - next;
        let others: Vec<usize> = (0..column.height_weights.len())
          .filter(|index| *index != window_index)
          .collect();
        let old_total: f64 = others
          .iter()
          .map(|index| column.height_weights[*index])
          .sum();
        for index in others {
          column.height_weights[index] = if old_total > 0.0 {
            column.height_weights[index] / old_total * remainder
          } else {
            remainder / (column.height_weights.len() - 1) as f64
          };
        }
      }
      _ => {}
    }
  }

  /// Apply a user's native edge-resize to the persistent tiled geometry.
  /// Position-only drags are deliberately ignored and snap back to the
  /// slot.
  pub fn adopt_user_resize(
    &mut self,
    focused: HWND,
    width_delta: i32,
    height_delta: i32,
    monitor: Rect,
    min_width: i32,
    min_height: i32,
  ) {
    let id = focused as isize;
    let Some((column_index, window_index)) =
      self.columns.iter().enumerate().find_map(|(ci, column)| {
        column
          .windows
          .iter()
          .position(|window| *window == id)
          .map(|wi| (ci, wi))
      })
    else {
      return;
    };

    if width_delta != 0 {
      let column = &mut self.columns[column_index];
      column.width = (column.width + width_delta)
        .clamp(min_width.min(monitor.width).max(1), monitor.width.max(1));
      self.last_resized_width = Some(column.width);
    }
    if height_delta != 0 && self.columns[column_index].windows.len() > 1 {
      let column = &mut self.columns[column_index];
      let count = column.windows.len();
      if column.height_weights.len() != count {
        column.height_weights = vec![1.0 / count as f64; count];
      }
      let min_weight = (min_height.max(1) as f64
        / monitor.height.max(1) as f64)
        .clamp(0.01, 0.49);
      let selected = (column.height_weights[window_index]
        + height_delta as f64 / monitor.height.max(1) as f64)
        .clamp(min_weight, 1.0 - min_weight * (count - 1) as f64);
      let old_others: f64 = column
        .height_weights
        .iter()
        .enumerate()
        .filter(|(i, _)| *i != window_index)
        .map(|(_, w)| *w)
        .sum();
      column.height_weights[window_index] = selected;
      for index in 0..count {
        if index != window_index {
          column.height_weights[index] = if old_others > 0.0 {
            column.height_weights[index] / old_others * (1.0 - selected)
          } else {
            (1.0 - selected) / (count - 1) as f64
          };
        }
      }
    }
  }
}

fn normalize_weights(weights: &mut [f64]) {
  if weights.is_empty() {
    return;
  }
  let total: f64 = weights.iter().sum();
  if total <= 0.0 {
    weights.fill(1.0 / weights.len() as f64);
  } else {
    for weight in weights {
      *weight /= total;
    }
  }
}

impl LayoutEngine {
  pub fn new() -> Self {
    Self {
      layout_mode: "Scrolling".to_string(),
      outer_top: 12,
      outer_bottom: 12,
      outer_left: 12,
      outer_right: 12,
      inner_gap: 8,
      min_width: 240,
      min_height: 180,
      vertical_pairs: HashMap::new(),
      window_sizes: HashMap::new(),
    }
  }

  /// Computes the tiled bounding boxes for a list of non-floating windows
  /// within monitor bounds
  pub fn compute_layout(
    &self,
    screen_bounds: Rect,
    windows: &[HWND],
  ) -> Vec<(HWND, Rect)> {
    let count = windows.len();
    if count == 0 {
      return Vec::new();
    }

    // Usable area after applying outer gaps
    // Clamp configured margins to the actual work area. A stale or
    // hand-edited config must never produce rectangles beyond the
    // monitor or negative-sized windows.
    let left = self.outer_left.clamp(0, (screen_bounds.width - 1).max(0));
    let right = self
      .outer_right
      .clamp(0, (screen_bounds.width - left - 1).max(0));
    let top = self.outer_top.clamp(0, (screen_bounds.height - 1).max(0));
    let bottom = self
      .outer_bottom
      .clamp(0, (screen_bounds.height - top - 1).max(0));
    let usable_x = screen_bounds.x + left;
    let usable_y = screen_bounds.y + top;
    let usable_w = (screen_bounds.width - left - right).max(1);
    let usable_h = (screen_bounds.height - top - bottom).max(1);

    let usable_rect = Rect::new(usable_x, usable_y, usable_w, usable_h);

    match self.layout_mode.to_uppercase().as_str() {
      "COLUMNS" => self.compute_columns(usable_rect, windows),
      "ROWS" => self.compute_rows(usable_rect, windows),
      "COSMIC" | "MASTERSTACK" | "MASTER-STACK" => {
        self.compute_master_stack(usable_rect, windows)
      }
      "SCROLLING" | "SCROLL" | "PAPERWM" => {
        self.compute_scrolling(usable_rect, windows)
      }
      "FLOATING" => Vec::new(), // Do not resize floating windows
      _ => self.compute_bsp(usable_rect, windows),
    }
  }

  /// COSMIC-inspired master and stack: the first window gets a primary
  /// column, with remaining windows arranged vertically in the secondary
  /// column.
  fn compute_master_stack(
    &self,
    bounds: Rect,
    windows: &[HWND],
  ) -> Vec<(HWND, Rect)> {
    if windows.len() < 2 {
      return self.compute_columns(bounds, windows);
    }
    let gap = self.inner_gap.clamp(0, bounds.width.saturating_sub(2));
    let split_width = bounds.width.saturating_sub(gap).max(2);
    let master_width =
      ((split_width * 55) / 100).clamp(1, split_width - 1);
    let stack_x = bounds.x + master_width + gap;
    let stack_width = (bounds.width - master_width - gap).max(1);
    let stack_count = (windows.len() - 1) as i32;
    let stack_gap = self.inner_gap.clamp(
      0,
      (bounds.height - stack_count).max(0) / (stack_count - 1).max(1),
    );
    let row_height =
      (bounds.height - stack_gap * (stack_count - 1)) / stack_count;

    let mut result = vec![(
      windows[0],
      Rect::new(bounds.x, bounds.y, master_width, bounds.height),
    )];
    let mut y = bounds.y;
    for (index, hwnd) in windows.iter().skip(1).enumerate() {
      let height = if index == windows.len() - 2 {
        bounds.y + bounds.height - y
      } else {
        row_height
      };
      result
        .push((*hwnd, Rect::new(stack_x, y, stack_width, height.max(1))));
      y += height + stack_gap;
    }
    result
  }

  /// PaperWM-inspired horizontal strip. Each window is a full-height
  /// column; the foreground window is centered in the viewport so
  /// Alt+Tab and clicks can bring any column into view without changing
  /// the workspace.
  fn compute_scrolling(
    &self,
    bounds: Rect,
    windows: &[HWND],
  ) -> Vec<(HWND, Rect)> {
    let foreground = unsafe {
      windows_sys::Win32::UI::WindowsAndMessaging::GetForegroundWindow()
    };
    let mut groups: Vec<Vec<HWND>> = Vec::new();
    let mut assigned = std::collections::HashSet::new();
    for &hwnd in windows {
      if assigned.contains(&(hwnd as isize)) {
        continue;
      }
      let mate = self.vertical_pairs.get(&(hwnd as isize)).copied();
      let mut group = vec![hwnd];
      assigned.insert(hwnd as isize);
      if let Some(mate) = mate {
        if let Some(&mate_hwnd) = windows
          .iter()
          .find(|candidate| **candidate as isize == mate)
        {
          group.push(mate_hwnd);
          assigned.insert(mate);
        }
      }
      groups.push(group);
    }
    let focused_index = groups
      .iter()
      .position(|group| group.contains(&foreground))
      .unwrap_or(0);
    let default_width = ((bounds.width * 3) / 4)
      .max(self.min_width)
      .min(bounds.width.max(1));
    let widths: Vec<i32> = groups
      .iter()
      .map(|group| {
        self
          .window_sizes
          .get(&(group[0] as isize))
          .map(|size| size.0)
          .filter(|width| *width > 0)
          .unwrap_or(default_width)
          .clamp(
            self.min_width.min(bounds.width).max(1),
            bounds.width.max(1),
          )
      })
      .collect();
    let mut xs = vec![bounds.x; groups.len()];
    if !groups.is_empty() {
      xs[focused_index] =
        bounds.x + (bounds.width - widths[focused_index]) / 2;
      for index in (0..focused_index).rev() {
        xs[index] = xs[index + 1] - widths[index] - self.inner_gap.max(0);
      }
      for index in focused_index + 1..groups.len() {
        xs[index] =
          xs[index - 1] + widths[index - 1] + self.inner_gap.max(0);
      }
    }
    let mut result = Vec::with_capacity(windows.len());
    for (index, group) in groups.iter().enumerate() {
      let x = xs[index];
      let column_width = widths[index];
      if group.len() == 2 {
        let gap = self.inner_gap.clamp(0, (bounds.height - 2).max(0));
        let available_height = (bounds.height - gap).max(2);
        let default_top = available_height / 2;
        let top_height = self
          .window_sizes
          .get(&(group[0] as isize))
          .map(|size| size.1)
          .filter(|height| *height > 0)
          .unwrap_or(default_top)
          .clamp(1, available_height - 1);
        result.push((
          group[0],
          Rect::new(x, bounds.y, column_width, top_height.max(1)),
        ));
        result.push((
          group[1],
          Rect::new(
            x,
            bounds.y + top_height + gap,
            column_width,
            (bounds.height - top_height - gap).max(1),
          ),
        ));
      } else {
        let height = self
          .window_sizes
          .get(&(group[0] as isize))
          .map(|size| size.1)
          .filter(|height| *height > 0)
          .unwrap_or(bounds.height)
          .clamp(
            self.min_height.min(bounds.height).max(1),
            bounds.height.max(1),
          );
        result.push((
          group[0],
          Rect::new(
            x,
            bounds.y + (bounds.height - height) / 2,
            column_width,
            height,
          ),
        ));
      }
    }
    result
  }

  /// Binary Space Partitioning (BSP) layout
  fn compute_bsp(
    &self,
    bounds: Rect,
    windows: &[HWND],
  ) -> Vec<(HWND, Rect)> {
    let mut results = Vec::new();
    let mut rects = vec![bounds];

    for _ in 1..windows.len() {
      if let Some(target) = rects.pop() {
        let split_horizontal = target.width >= target.height;

        if split_horizontal {
          let w1 = (target.width - self.inner_gap) / 2;
          let w2 = target.width - self.inner_gap - w1;

          let r1 = Rect::new(target.x, target.y, w1, target.height);
          let r2 = Rect::new(
            target.x + w1 + self.inner_gap,
            target.y,
            w2,
            target.height,
          );
          rects.push(r1);
          rects.push(r2);
        } else {
          let h1 = (target.height - self.inner_gap) / 2;
          let h2 = target.height - self.inner_gap - h1;

          let r1 = Rect::new(target.x, target.y, target.width, h1);
          let r2 = Rect::new(
            target.x,
            target.y + h1 + self.inner_gap,
            target.width,
            h2,
          );
          rects.push(r1);
          rects.push(r2);
        }
      }
    }

    for (i, &hwnd) in windows.iter().enumerate() {
      if i < rects.len() {
        results.push((hwnd, rects[i]));
      }
    }

    results
  }

  /// Columns layout (side-by-side vertical columns)
  fn compute_columns(
    &self,
    bounds: Rect,
    windows: &[HWND],
  ) -> Vec<(HWND, Rect)> {
    let count = windows.len() as i32;
    let gap = self
      .inner_gap
      .clamp(0, (bounds.width - count).max(0) / (count - 1).max(1));
    let total_inner = (count - 1) * gap;
    let col_width = (bounds.width - total_inner) / count;

    let mut results = Vec::new();
    let mut cur_x = bounds.x;

    for (i, &hwnd) in windows.iter().enumerate() {
      let is_last = i == windows.len() - 1;
      let w = if is_last {
        bounds.x + bounds.width - cur_x
      } else {
        col_width
      };

      results.push((hwnd, Rect::new(cur_x, bounds.y, w, bounds.height)));
      cur_x += w + gap;
    }

    results
  }

  /// Rows layout (stacked horizontal rows)
  fn compute_rows(
    &self,
    bounds: Rect,
    windows: &[HWND],
  ) -> Vec<(HWND, Rect)> {
    let count = windows.len() as i32;
    let gap = self
      .inner_gap
      .clamp(0, (bounds.height - count).max(0) / (count - 1).max(1));
    let total_inner = (count - 1) * gap;
    let row_height = (bounds.height - total_inner) / count;

    let mut results = Vec::new();
    let mut cur_y = bounds.y;

    for (i, &hwnd) in windows.iter().enumerate() {
      let is_last = i == windows.len() - 1;
      let h = if is_last {
        bounds.y + bounds.height - cur_y
      } else {
        row_height
      };

      results.push((hwnd, Rect::new(bounds.x, cur_y, bounds.width, h)));
      cur_y += h + gap;
    }

    results
  }
}
