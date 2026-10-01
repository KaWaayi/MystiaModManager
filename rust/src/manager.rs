use crate::error::{AppError, Result};
use crate::paths::{manager_json_path, profiles_dir};
use serde::{Deserialize, Serialize};
use std::path::Path;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ManagerSettings {
    pub game_path: String,
    pub manager_path: String,
    pub config_root: String,
    #[serde(default)]
    pub current_profile: String,
    #[serde(default)]
    pub bepinex_build_id: Option<u32>,
    #[serde(default)]
    pub bepinex_version: Option<String>,
}

impl ManagerSettings {
    pub fn load(config_root: &Path) -> Result<Self> {
        let path = manager_json_path(config_root);
        if !path.is_file() {
            return Err(AppError::msg("尚未完成首次配置（缺少 manager.json）"));
        }
        let text = std::fs::read_to_string(&path)?;
        Ok(serde_json::from_str(&text)?)
    }

    pub fn save(&self, config_root: &Path) -> Result<()> {
        std::fs::create_dir_all(config_root)?;
        std::fs::create_dir_all(profiles_dir(config_root))?;
        let path = manager_json_path(config_root);
        let text = serde_json::to_string_pretty(self)?;
        std::fs::write(path, text)?;
        Ok(())
    }
}

pub fn init_or_load(
    game_path: &str,
    manager_path: &str,
    config_root: &str,
) -> Result<ManagerSettings> {
    let root = Path::new(config_root);
    let path = manager_json_path(root);
    if path.is_file() {
        let mut s = ManagerSettings::load(root)?;
        // keep paths in sync if installer changed them
        s.game_path = game_path.to_string();
        s.manager_path = manager_path.to_string();
        s.config_root = config_root.to_string();
        s.save(root)?;
        Ok(s)
    } else {
        let s = ManagerSettings {
            game_path: game_path.to_string(),
            manager_path: manager_path.to_string(),
            config_root: config_root.to_string(),
            current_profile: String::new(),
            bepinex_build_id: None,
            bepinex_version: None,
        };
        s.save(root)?;
        Ok(s)
    }
}
