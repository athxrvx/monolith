use std::{fs, io::Read, path::PathBuf};

use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct EngineConfig {
  #[serde(default = "default_true")]
  pub engine_startup: bool,

  #[serde(default)]
  pub run_launcher_admin: bool,

  #[serde(default)]
  pub hide_taskbar: bool,

  #[serde(default = "default_info")]
  pub log_level: String,

  #[serde(default = "default_true")]
  pub wrap_around: bool,

  #[serde(default = "default_true")]
  pub focus_follows_cursor: bool,

  #[serde(default = "default_scrolling")]
  pub layout_mode: String,

  #[serde(default = "default_outer_gap")]
  pub outer_gaps_top: i32,

  #[serde(default = "default_outer_gap")]
  pub outer_gaps_bottom: i32,

  #[serde(default = "default_outer_gap")]
  pub outer_gaps_left: i32,

  #[serde(default = "default_outer_gap")]
  pub outer_gaps_right: i32,

  #[serde(default = "default_inner_gap")]
  pub inner_gaps: i32,

  #[serde(default)]
  pub aspect_ratio_lock: bool,

  #[serde(default = "default_resize_step")]
  pub resize_step_percentage: i32,

  #[serde(default = "default_min_width")]
  pub min_width: i32,

  #[serde(default = "default_min_height")]
  pub min_height: i32,

  #[serde(default)]
  pub window_rules: Vec<WindowRuleConfig>,

  #[serde(default = "default_true")]
  pub enable_active_border: bool,

  #[serde(default = "default_active_color")]
  pub active_border_color: String,

  #[serde(default = "default_inactive_color")]
  pub inactive_border_color: String,

  #[serde(default = "default_border_thickness")]
  pub border_thickness: i32,

  #[serde(default = "default_acrylic")]
  pub backdrop_material: String,

  #[serde(default = "default_opacity")]
  pub launcher_opacity: i32,

  #[serde(default = "default_true")]
  pub enable_animations: bool,

  #[serde(default = "default_animation_mode")]
  pub animation_mode: String,

  #[serde(default = "default_duration")]
  pub reflow_duration: i32,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct WindowRuleConfig {
  #[serde(default)]
  pub process_name: String,

  #[serde(default = "default_regex")]
  pub title_regex: String,

  #[serde(default = "default_action")]
  pub action: String,

  #[serde(default = "default_true")]
  pub manage_border_visibility: bool,
}

fn default_true() -> bool {
  true
}
fn default_info() -> String {
  "Info".to_string()
}
fn default_scrolling() -> String {
  "Scrolling".to_string()
}
fn default_outer_gap() -> i32 {
  12
}
fn default_inner_gap() -> i32 {
  8
}
fn default_resize_step() -> i32 {
  5
}
fn default_min_width() -> i32 {
  240
}
fn default_min_height() -> i32 {
  180
}
fn default_active_color() -> String {
  "#60CDFF".to_string()
}
fn default_inactive_color() -> String {
  "#26FFFFFF".to_string()
}
fn default_border_thickness() -> i32 {
  2
}
fn default_acrylic() -> String {
  "Acrylic (Frosted Glass)".to_string()
}
fn default_opacity() -> i32 {
  60
}
fn default_duration() -> i32 {
  120
}
fn default_animation_mode() -> String {
  "Smooth".to_string()
}
fn default_regex() -> String {
  ".*".to_string()
}
fn default_action() -> String {
  "Force Tile".to_string()
}

impl Default for EngineConfig {
  fn default() -> Self {
    Self {
      engine_startup: true,
      run_launcher_admin: false,
      hide_taskbar: false,
      log_level: default_info(),
      wrap_around: true,
      focus_follows_cursor: true,
      layout_mode: default_scrolling(),
      outer_gaps_top: 12,
      outer_gaps_bottom: 12,
      outer_gaps_left: 12,
      outer_gaps_right: 12,
      inner_gaps: 8,
      aspect_ratio_lock: false,
      resize_step_percentage: 5,
      min_width: 240,
      min_height: 180,
      window_rules: vec![
        WindowRuleConfig {
          process_name: "spotify.exe".to_string(),
          title_regex: ".*".to_string(),
          action: "Force Tile".to_string(),
          manage_border_visibility: true,
        },
        WindowRuleConfig {
          process_name: "taskmgr.exe".to_string(),
          title_regex: ".*".to_string(),
          action: "Force Float".to_string(),
          manage_border_visibility: false,
        },
      ],
      enable_active_border: true,
      active_border_color: default_active_color(),
      inactive_border_color: default_inactive_color(),
      border_thickness: 2,
      backdrop_material: default_acrylic(),
      launcher_opacity: 60,
      enable_animations: true,
      animation_mode: default_animation_mode(),
      reflow_duration: 120,
    }
  }
}

pub fn get_config_path() -> PathBuf {
  let user_profile = std::env::var_os("USERPROFILE")
    .or_else(|| std::env::var_os("HOME"))
    .map(PathBuf::from)
    .unwrap_or_else(|| PathBuf::from("."));
  user_profile
    .join(".config")
    .join("monolith")
    .join("config.json")
}

pub fn load_config() -> EngineConfig {
  let path = get_config_path();
  let legacy_path = std::env::var_os("APPDATA")
    .map(PathBuf::from)
    .map(|app_data| app_data.join("Monolith").join("config.json"));

  let current_content = read_bounded_config(&path);
  let (content, is_legacy) = match current_content {
    Ok(content) => (Some(content), false),
    Err(error) if error.kind() == std::io::ErrorKind::InvalidData => {
      return EngineConfig::default();
    }
    Err(_) => match legacy_path.as_ref().map(read_bounded_config) {
      Some(Ok(content)) => (Some(content), true),
      Some(Err(error)) if error.kind() == std::io::ErrorKind::InvalidData => {
        return EngineConfig::default();
      }
      _ => (None, true),
    },
  };

  if let Some(content) = content {
    if let Ok(cfg) = serde_json::from_str::<EngineConfig>(&content) {
      if is_legacy {
        let _ = save_config(&cfg);
      }
      return cfg;
    }
  }

  let default = EngineConfig::default();
  let _ = save_config(&default);
  default
}

const MAX_CONFIG_BYTES: u64 = 1024 * 1024;

fn read_bounded_config(path: &PathBuf) -> std::io::Result<String> {
  let file = fs::File::open(path)?;
  let mut content = String::new();
  file.take(MAX_CONFIG_BYTES + 1).read_to_string(&mut content)?;
  if content.len() as u64 > MAX_CONFIG_BYTES {
    return Err(std::io::Error::new(
      std::io::ErrorKind::InvalidData,
      "configuration exceeds 1 MiB limit",
    ));
  }
  Ok(content)
}

pub fn save_config(cfg: &EngineConfig) -> anyhow::Result<()> {
  let path = get_config_path();
  if let Some(parent) = path.parent() {
    let _ = fs::create_dir_all(parent);
  }
  let json = serde_json::to_string_pretty(cfg)?;
  fs::write(path, json)?;
  Ok(())
}
