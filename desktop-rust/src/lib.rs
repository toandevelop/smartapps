use serde::{Deserialize, Serialize};
use std::fs;
use std::path::PathBuf;
use tauri::menu::{Menu, MenuItem};
use tauri::tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent};
use tauri::{AppHandle, Manager, WebviewUrl, WebviewWindowBuilder};

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DesktopInfo {
    pub os: String,
    pub arch: String,
    pub runtime: String,
    pub app_version: String,
    pub data_dir: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ServerConfig {
    pub web_url: String,
    pub api_url: String,
}

impl Default for ServerConfig {
    fn default() -> Self {
        Self {
            web_url: "http://localhost:4100".to_string(),
            api_url: "http://localhost:5200".to_string(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct OfflineMutation {
    pub id: String,
    pub app_code: String,
    pub table_id: String,
    pub method: String,
    pub endpoint: String,
    pub payload: serde_json::Value,
    pub created_at: String,
}

fn data_file(app: &AppHandle, name: &str) -> Result<PathBuf, String> {
    let dir = app.path().app_local_data_dir().map_err(|e| e.to_string())?;
    fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    Ok(dir.join(name))
}

#[tauri::command]
fn get_desktop_info(app: AppHandle) -> DesktopInfo {
    let data_dir = app
        .path()
        .app_local_data_dir()
        .map(|p| p.to_string_lossy().to_string())
        .unwrap_or_default();

    DesktopInfo {
        os: std::env::consts::OS.to_string(),
        arch: std::env::consts::ARCH.to_string(),
        runtime: "Rust + Tauri 2 (Offline Sync + System Tray)".to_string(),
        app_version: app.package_info().version.to_string(),
        data_dir,
    }
}

#[tauri::command]
fn get_server_config(app: AppHandle) -> ServerConfig {
    if let Ok(path) = data_file(&app, "server_config.json") {
        if let Ok(raw) = fs::read_to_string(path) {
            if let Ok(cfg) = serde_json::from_str::<ServerConfig>(&raw) {
                return cfg;
            }
        }
    }
    ServerConfig::default()
}

#[tauri::command]
fn set_server_config(app: AppHandle, web_url: String, api_url: String) -> Result<ServerConfig, String> {
    let cfg = ServerConfig {
        web_url: web_url.trim().trim_end_matches('/').to_string(),
        api_url: api_url.trim().trim_end_matches('/').to_string(),
    };
    let path = data_file(&app, "server_config.json")?;
    let json = serde_json::to_string_pretty(&cfg).map_err(|e| e.to_string())?;
    fs::write(path, json).map_err(|e| e.to_string())?;
    Ok(cfg)
}

#[tauri::command]
fn open_app_window(
    app: AppHandle,
    app_code: String,
    app_name: String,
    base_url: Option<String>,
) -> Result<String, String> {
    let clean_code: String = app_code
        .chars()
        .filter(|c| c.is_ascii_alphanumeric() || *c == '_' || *c == '-')
        .collect();
    let label = format!("app-{}", clean_code.to_lowercase());

    if let Some(existing) = app.get_webview_window(&label) {
        existing.set_focus().map_err(|e| e.to_string())?;
        return Ok(format!("Focused existing window {}", label));
    }

    let origin = base_url.unwrap_or_else(|| get_server_config(app.clone()).web_url);
    let target_url = format!("{}/apps/{}", origin.trim_end_matches('/'), clean_code);
    let parsed_url: tauri::Url = target_url.parse().map_err(|e| format!("Invalid URL: {}", e))?;

    WebviewWindowBuilder::new(&app, &label, WebviewUrl::External(parsed_url))
        .title(format!("{} ({}) — SmartApps Desktop", app_name, clean_code))
        .inner_size(1280.0, 820.0)
        .min_inner_size(900.0, 620.0)
        .center()
        .build()
        .map_err(|e| e.to_string())?;

    Ok(format!("Opened native window {}", label))
}

#[tauri::command]
fn save_offline_snapshot(app: AppHandle, app_code: String, json_payload: String) -> Result<String, String> {
    let dir = app
        .path()
        .app_local_data_dir()
        .map_err(|e| e.to_string())?
        .join("snapshots");
    fs::create_dir_all(&dir).map_err(|e| e.to_string())?;

    let file_path = dir.join(format!("{}.json", app_code.to_lowercase()));
    fs::write(&file_path, json_payload).map_err(|e| e.to_string())?;
    Ok(file_path.to_string_lossy().to_string())
}

#[tauri::command]
fn load_offline_snapshot(app: AppHandle, app_code: String) -> Result<String, String> {
    let file_path = app
        .path()
        .app_local_data_dir()
        .map_err(|e| e.to_string())?
        .join("snapshots")
        .join(format!("{}.json", app_code.to_lowercase()));

    if !file_path.exists() {
        return Err("Chưa có bản sao lưu cục bộ cho ứng dụng này.".to_string());
    }
    fs::read_to_string(file_path).map_err(|e| e.to_string())
}

fn read_queue_file(app: &AppHandle) -> Result<Vec<OfflineMutation>, String> {
    let path = data_file(app, "offline_queue.json")?;
    if !path.exists() {
        return Ok(vec![]);
    }
    let raw = fs::read_to_string(path).map_err(|e| e.to_string())?;
    serde_json::from_str(&raw).map_err(|e| e.to_string())
}

fn write_queue_file(app: &AppHandle, items: &[OfflineMutation]) -> Result<(), String> {
    let path = data_file(app, "offline_queue.json")?;
    let raw = serde_json::to_string_pretty(items).map_err(|e| e.to_string())?;
    fs::write(path, raw).map_err(|e| e.to_string())
}

#[tauri::command]
fn queue_offline_mutation(app: AppHandle, item: OfflineMutation) -> Result<Vec<OfflineMutation>, String> {
    let mut items = read_queue_file(&app)?;
    items.push(item);
    write_queue_file(&app, &items)?;
    Ok(items)
}

#[tauri::command]
fn list_offline_queue(app: AppHandle) -> Result<Vec<OfflineMutation>, String> {
    read_queue_file(&app)
}

#[tauri::command]
fn remove_offline_mutation(app: AppHandle, id: String) -> Result<Vec<OfflineMutation>, String> {
    let mut items = read_queue_file(&app)?;
    items.retain(|x| x.id != id);
    write_queue_file(&app, &items)?;
    Ok(items)
}

#[tauri::command]
fn clear_offline_queue(app: AppHandle) -> Result<(), String> {
    write_queue_file(&app, &[])
}

#[tauri::command]
fn export_report_file(app: AppHandle, filename: String, content: String) -> Result<String, String> {
    let dir = app
        .path()
        .download_dir()
        .or_else(|_| app.path().app_local_data_dir())
        .map_err(|e| e.to_string())?;
    fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    let safe_name: String = filename
        .chars()
        .filter(|c| c.is_ascii_alphanumeric() || matches!(c, '.' | '_' | '-'))
        .collect();
    let target = dir.join(if safe_name.is_empty() { "smartapps_export.txt".to_string() } else { safe_name });
    fs::write(&target, content).map_err(|e| e.to_string())?;
    Ok(target.to_string_lossy().to_string())
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .setup(|app| {
            let show_i = MenuItem::with_id(app, "show", "Mở SmartApps Hub", true, None::<&str>)?;
            let hide_i = MenuItem::with_id(app, "hide", "Thu nhỏ xuống khay", true, None::<&str>)?;
            let quit_i = MenuItem::with_id(app, "quit", "Thoát SmartApps Desktop", true, None::<&str>)?;
            let menu = Menu::with_items(app, &[&show_i, &hide_i, &quit_i])?;

            let _tray = TrayIconBuilder::new()
                .menu(&menu)
                .on_menu_event(|app, event| match event.id.as_ref() {
                    "show" => {
                        if let Some(w) = app.get_webview_window("main") {
                            let _ = w.show();
                            let _ = w.set_focus();
                        }
                    }
                    "hide" => {
                        if let Some(w) = app.get_webview_window("main") {
                            let _ = w.hide();
                        }
                    }
                    "quit" => app.exit(0),
                    _ => {}
                })
                .on_tray_icon_event(|tray, event| {
                    if let TrayIconEvent::Click {
                        button: MouseButton::Left,
                        button_state: MouseButtonState::Up,
                        ..
                    } = event
                    {
                        if let Some(w) = tray.app_handle().get_webview_window("main") {
                            let _ = w.show();
                            let _ = w.set_focus();
                        }
                    }
                })
                .build(app)?;

            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            get_desktop_info,
            get_server_config,
            set_server_config,
            open_app_window,
            save_offline_snapshot,
            load_offline_snapshot,
            queue_offline_mutation,
            list_offline_queue,
            remove_offline_mutation,
            clear_offline_queue,
            export_report_file
        ])
        .run(tauri::generate_context!())
        .expect("error while running SmartApps Rust Desktop application");
}
