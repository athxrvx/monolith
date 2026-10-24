use std::{io, sync::Arc};

use serde::{Deserialize, Serialize};
use tokio::{
  io::{AsyncBufRead, AsyncBufReadExt, AsyncWriteExt, BufReader},
  net::windows::named_pipe::{NamedPipeServer, ServerOptions},
  sync::{broadcast, Mutex},
};
use tracing::{error, info};

use crate::{config::EngineConfig, tiling::WindowNode};

pub const PIPE_NAME: &str = r"\\.\pipe\monolith_ipc";
const MAX_IPC_LINE_BYTES: usize = 64 * 1024;
const MAX_IPC_CLIENTS: usize = 16;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct IpcRequest {
  pub source: String,
  pub command: String,
  #[serde(default)]
  pub args: serde_json::Value,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct EngineStatePayload {
  pub active_workspace: usize,
  pub total_workspaces: usize,
  pub layout_mode: String,
  pub outer_gaps: (i32, i32, i32, i32),
  pub inner_gaps: i32,
  pub windows: Vec<WindowNode>,
}

pub struct IpcServer {
  state_tx: broadcast::Sender<String>,
}

impl IpcServer {
  pub fn new(
  ) -> (Self, broadcast::Sender<String>, broadcast::Receiver<String>) {
    let (tx, rx) = broadcast::channel(32);
    (
      Self {
        state_tx: tx.clone(),
      },
      tx,
      rx,
    )
  }

  pub async fn run(
    &self,
    config: Arc<Mutex<EngineConfig>>,
    active_ws: Arc<std::sync::atomic::AtomicUsize>,
    action_tx: tokio::sync::mpsc::Sender<IpcRequest>,
  ) -> anyhow::Result<()> {
    info!("Starting Monolith IPC Named Pipe server at {}", PIPE_NAME);

    let mut first = true;
    let clients = Arc::new(tokio::sync::Semaphore::new(MAX_IPC_CLIENTS));
    loop {
      let permit = clients.clone().acquire_owned().await?;
      let server = match ServerOptions::new()
        .first_pipe_instance(first)
        .create(PIPE_NAME)
      {
        Ok(s) => {
          first = false;
          s
        }
        Err(e) => {
          error!("Failed to create named pipe server instance: {}", e);
          tokio::time::sleep(tokio::time::Duration::from_millis(500))
            .await;
          continue;
        }
      };

      // Wait for client connection
      if let Err(e) = server.connect().await {
        error!("Error accepting pipe connection: {}", e);
        continue;
      }

      let client_config = config.clone();
      let client_ws = active_ws.clone();
      let client_action = action_tx.clone();
      let mut state_rx = self.state_tx.subscribe();

      tokio::spawn(async move {
        let _permit = permit;
        if let Err(e) = handle_client(
          server,
          client_config,
          client_ws,
          client_action,
          &mut state_rx,
        )
        .await
        {
          info!("Client disconnected from IPC pipe: {}", e);
        }
      });
    }
  }
}

async fn handle_client(
  server: NamedPipeServer,
  config: Arc<Mutex<EngineConfig>>,
  active_ws: Arc<std::sync::atomic::AtomicUsize>,
  action_tx: tokio::sync::mpsc::Sender<IpcRequest>,
  state_rx: &mut broadcast::Receiver<String>,
) -> anyhow::Result<()> {
  let (reader, mut writer) = tokio::io::split(server);
  let mut reader = BufReader::new(reader);
  let mut line = Vec::with_capacity(1024);

  loop {
    tokio::select! {
        result = read_bounded_line(&mut reader, &mut line) => {
            match result? {
                Some(payload) => {
                    if let Ok(req) = serde_json::from_slice::<IpcRequest>(&payload) {
                        match req.command.as_str() {
                            "GET_STATE" => {
                                let cfg = config.lock().await;
                                let state = EngineStatePayload {
                                    active_workspace: active_ws.load(std::sync::atomic::Ordering::SeqCst),
                                    total_workspaces: 9,
                                    layout_mode: cfg.layout_mode.clone(),
                                    outer_gaps: (cfg.outer_gaps_top, cfg.outer_gaps_bottom, cfg.outer_gaps_left, cfg.outer_gaps_right),
                                    inner_gaps: cfg.inner_gaps,
                                    windows: Vec::new(),
                                };
                                let resp = serde_json::to_string(&state)? + "\n";
                                writer.write_all(resp.as_bytes()).await?;
                            }
                            "SET_STATE" | "FOCUS_WORKSPACE" | "FOCUS_WORKSPACE_DELTA" | "FOCUS_DIRECTION" | "MOVE_WINDOW_DIRECTION" | "MOVE_WINDOW" | "MOVE_WINDOW_WORKSPACE_DELTA" | "TOGGLE_FLOATING" | "TOGGLE_VERTICAL_TILE" | "RESIZE_WINDOW" | "TOGGLE_MONOCLE" | "RELOAD_CONFIG" => {
                                let _ = action_tx.send(req).await;
                            }
                            _ => {}
                        }
                    }
                }
                None => break,
            }
        }
        Ok(msg) = state_rx.recv() => {
            let formatted = msg + "\n";
            if writer.write_all(formatted.as_bytes()).await.is_err() {
                break;
            }
        }
    }
  }

  Ok(())
}

async fn read_bounded_line<R: AsyncBufRead + Unpin>(
  reader: &mut R,
  line: &mut Vec<u8>,
) -> io::Result<Option<Vec<u8>>> {
  line.clear();
  loop {
    let available = reader.fill_buf().await?;
    if available.is_empty() {
      return if line.is_empty() { Ok(None) } else { Ok(Some(std::mem::take(line))) };
    }

    let newline = available.iter().position(|byte| *byte == b'\n');
    let take = newline.unwrap_or(available.len());
    let content_len = if newline.is_some() && take > 0 && available[take - 1] == b'\r' {
      take - 1
    } else {
      take
    };
    if line.len().saturating_add(content_len) > MAX_IPC_LINE_BYTES {
      return Err(io::Error::new(io::ErrorKind::InvalidData, "IPC line exceeds size limit"));
    }
    line.extend_from_slice(&available[..content_len]);
    let consumed = take + usize::from(newline.is_some());
    reader.consume(consumed);

    if newline.is_some() {
      return Ok(Some(std::mem::take(line)));
    }
  }
}
