#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import sys
import os
import json
import urllib.request
import ssl
import ctypes
from ctypes import wintypes
import time
import shutil
import threading
import queue
import tkinter as tk
from tkinter import ttk
import datetime
import re

REPO_LIGHT_OWNER = "AfishMW"
REPO_LIGHT_NAME   = "HSG_MOD"

REPO_LID_OWNER   = "hvtXsvc"
REPO_LID_NAME    = "LightInDark_API"

MIRROR_PREFIX = "https://ghproxy.com/"
LOG_FILE = "update.log"

def log_message(msg):
    try:
        with open(LOG_FILE, "a", encoding="utf-8") as f:
            f.write(f"[{datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')}] {msg}\n")
    except:
        pass

def get_program_dir():
    if getattr(sys, 'frozen', False):
        return os.path.dirname(sys.executable)
    else:
        return os.path.dirname(os.path.abspath(__file__))

def find_bepinex(start_dir):
    for _ in range(4):
        bep = os.path.join(start_dir, "BepInEx")
        if os.path.isdir(bep):
            return bep
        parent = os.path.dirname(start_dir)
        if parent == start_dir:
            break
        start_dir = parent
    return None

def is_process_running(name):
    TH32CS_SNAPPROCESS = 0x00000002
    
    class PROCESSENTRY32W(ctypes.Structure):
        _fields_ = [
            ("dwSize", wintypes.DWORD),
            ("cntUsage", wintypes.DWORD),
            ("th32ProcessID", wintypes.DWORD),
            ("th32DefaultHeapID", ctypes.POINTER(ctypes.c_ulong)),
            ("th32ModuleID", wintypes.DWORD),
            ("cntThreads", wintypes.DWORD),
            ("th32ParentProcessID", wintypes.DWORD),
            ("pcPriClassBase", ctypes.c_long),          # 原为 ctypes.LONG
            ("dwFlags", wintypes.DWORD),
            ("szExeFile", wintypes.WCHAR * 260)
        ]

    kernel32 = ctypes.windll.kernel32
    snap = kernel32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    if snap == -1:
        return False

    entry = PROCESSENTRY32W()
    entry.dwSize = ctypes.sizeof(PROCESSENTRY32W)
    found = False

    if kernel32.Process32FirstW(snap, ctypes.byref(entry)):
        while True:
            if entry.szExeFile.lower() == name.lower():
                found = True
                break
            if not kernel32.Process32NextW(snap, ctypes.byref(entry)):
                break

    kernel32.CloseHandle(snap)
    return found

def get_local_versions(plugins_dir):
    ver_file = os.path.join(plugins_dir, "version.json")
    if not os.path.isfile(ver_file):
        return None, None
    try:
        with open(ver_file, "r", encoding="utf-8") as f:
            data = json.load(f)
        return data.get("Light"), data.get("LightInDark")
    except:
        return None, None

def get_remote_version(owner, repo):
    url = f"https://api.github.com/repos/{owner}/{repo}/releases/latest"
    try:
        context = ssl._create_unverified_context()
        req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
        with urllib.request.urlopen(req, context=context, timeout=10) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            tag = data.get("tag_name", "")
            if tag.startswith("v"):
                tag = tag[1:]
            return tag
    except Exception as e:
        log_message(f"获取远程版本失败: {e}")
        return None

def version_compare(v1, v2):
    parts1 = [int(x) for x in v1.split(".")]
    parts2 = [int(x) for x in v2.split(".")]
    for a, b in zip(parts1, parts2):
        if a != b:
            return 1 if a > b else -1
    return len(parts1) - len(parts2)

def get_asset_download_urls(owner, repo):
    url = f"https://api.github.com/repos/{owner}/{repo}/releases/latest"
    try:
        context = ssl._create_unverified_context()
        req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
        with urllib.request.urlopen(req, context=context, timeout=10) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            assets = data.get("assets", [])
            result = {}
            for asset in assets:
                name = asset.get("name")
                download_url = asset.get("browser_download_url")
                if name and download_url:
                    result[name] = download_url
            return result
    except Exception as e:
        log_message(f"获取资产列表失败: {e}")
        return None

def download_file(url, dest_path, progress_callback=None, max_retries=3):
    for attempt in range(max_retries):
        try:
            context = ssl._create_unverified_context()
            req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
            with urllib.request.urlopen(req, context=context, timeout=30) as resp:
                total_size = int(resp.headers.get('content-length', 0))
                downloaded = 0
                chunk_size = 8192
                with open(dest_path, 'wb') as f:
                    while True:
                        chunk = resp.read(chunk_size)
                        if not chunk:
                            break
                        f.write(chunk)
                        downloaded += len(chunk)
                        if progress_callback:
                            progress_callback(downloaded, total_size)
                return True
        except Exception as e:
            log_message(f"下载失败 (尝试 {attempt+1}/{max_retries}): {e}")
            if attempt < max_retries - 1:
                time.sleep(1)
            else:
                return False
    return False
class ProgressWindow:
    def __init__(self, title="更新进度"):
        self.root = tk.Tk()
        self.root.title(title)
        self.root.geometry("400x150")
        self.root.resizable(False, False)
        self.root.protocol("WM_DELETE_WINDOW", self.on_close)

        self.status_label = tk.Label(self.root, text="准备下载...")
        self.status_label.pack(pady=10)

        self.progress_var = tk.DoubleVar()
        self.progress_bar = ttk.Progressbar(self.root, variable=self.progress_var, maximum=100)
        self.progress_bar.pack(fill=tk.X, padx=20, pady=10)

        self.detail_label = tk.Label(self.root, text="")
        self.detail_label.pack(pady=5)

        self.cancel_btn = tk.Button(self.root, text="取消", command=self.on_close)
        self.cancel_btn.pack(pady=10)

        self.finished = False
        self.canceled = False
        self.queue = queue.Queue()

    def on_close(self):
        self.canceled = True
        self.root.quit()

    def update_progress(self, value, status_text="", detail_text=""):
        self.queue.put(("progress", value, status_text, detail_text))

    def set_finished(self):
        self.queue.put(("finished",))

    def run(self):
        self.root.after(100, self.check_queue)
        self.root.mainloop()
        return not self.canceled

    def check_queue(self):
        try:
            while True:
                msg = self.queue.get_nowait()
                if msg[0] == "progress":
                    _, value, status, detail = msg
                    self.progress_var.set(value)
                    if status:
                        self.status_label.config(text=status)
                    if detail:
                        self.detail_label.config(text=detail)
                elif msg[0] == "finished":
                    self.finished = True
                    self.cancel_btn.config(state=tk.DISABLED)
                    self.status_label.config(text="下载完成！")
                    self.detail_label.config(text="")
        except queue.Empty:
            pass
        if not self.finished:
            self.root.after(100, self.check_queue)
        else:
            self.root.after(1500, self.root.quit)

def download_files_with_progress(assets, save_dir, progress_window, mirror=False):
    files_to_download = ["Light.dll", "LightInDark.dll"]
    total_files = len(files_to_download)
    completed = 0
    success = True
    failed_files = []

    for filename in files_to_download:
        if filename not in assets:
            log_message(f"资产中未找到 {filename}")
            failed_files.append(filename)
            success = False
            continue

        url = assets[filename]
        if mirror:
            if url.startswith("https://github.com/"):
                url = MIRROR_PREFIX + url
            else:
                url = MIRROR_PREFIX + url

        dest_path = os.path.join(save_dir, filename)
        log_message(f"开始下载 {filename} 从 {url}")
        progress_window.update_progress(completed / total_files * 100, f"正在下载 {filename}...", "")

        def progress_callback(downloaded, total):
            if total > 0:
                percent = (downloaded / total) * 100
                progress_window.update_progress(
                    (completed + (downloaded / total)) / total_files * 100,
                    f"正在下载 {filename}...",
                    f"{downloaded / 1024 / 1024:.1f} MB / {total / 1024 / 1024:.1f} MB"
                )

        ok = download_file(url, dest_path, progress_callback)
        if ok:
            completed += 1
            log_message(f"下载成功: {filename}")
        else:
            log_message(f"下载失败: {filename}")
            failed_files.append(filename)
            success = False
            break

    if success:
        progress_window.update_progress(100, "下载完成！", "所有文件已下载")
        progress_window.set_finished()
        return True, []
    else:
        return False, failed_files

def ask_update_confirmation():
    result = ctypes.windll.user32.MessageBoxW(
        0,
        "检测到新版本，是否立即更新？\n（游戏已退出，更新后将自动安装）",
        "更新确认",
        0x00000004 | 0x00000020
    )
    return result == 6

def show_error_message(msg):
    ctypes.windll.user32.MessageBoxW(0, msg, "更新错误", 0x00000010)

def show_info_message(msg):
    ctypes.windll.user32.MessageBoxW(0, msg, "提示", 0x00000040)

def listen_and_update():
    program_dir = get_program_dir()
    log_message("=== 监听更新模式启动 ===")

    bepinex_dir = find_bepinex(program_dir)
    if bepinex_dir is None:
        log_message("未找到 BepInEx 目录")
        print("not installed")
        return
    plugins_dir = os.path.join(bepinex_dir, "plugins")
    if not os.path.isdir(plugins_dir):
        log_message("plugins 目录不存在")
        print("not installed")
        return

    dll1 = os.path.join(plugins_dir, "Light.dll")
    dll2 = os.path.join(plugins_dir, "LightInDark.dll")
    if not (os.path.isfile(dll1) and os.path.isfile(dll2)):
        log_message("缺少 Light.dll 或 LightInDark.dll，视为未安装")
        print("not installed")
        return

    log_message("等待 Among Us.exe 退出...")
    while is_process_running("Among Us.exe"):
        time.sleep(2)
    log_message("游戏已退出")

    if not ask_update_confirmation():
        log_message("用户取消更新")
        print("canceled")
        return

    log_message("获取最新 Release 资产...")
    assets_light = get_asset_download_urls(REPO_LIGHT_OWNER, REPO_LIGHT_NAME)
    assets_lid = get_asset_download_urls(REPO_LID_OWNER, REPO_LID_NAME)
    if assets_light is None or assets_lid is None:
        log_message("获取资产失败")
        show_error_message("无法获取更新信息，请检查网络。")
        print("github error")
        return

    all_assets = {}
    all_assets.update(assets_light)
    all_assets.update(assets_lid)

    required = ["Light.dll", "LightInDark.dll"]
    missing = [f for f in required if f not in all_assets]
    if missing:
        log_message(f"资产中缺少: {missing}")
        show_error_message(f"Release 中缺少必要文件: {', '.join(missing)}")
        print("download failed")
        return

    temp_dir = os.path.join(program_dir, "temp_update")
    os.makedirs(temp_dir, exist_ok=True)

    progress_win = ProgressWindow()
    def download_thread():
        ok, failed = download_files_with_progress(all_assets, temp_dir, progress_win, mirror=False)
        if ok:
            try:
                shutil.copy2(os.path.join(temp_dir, "Light.dll"), plugins_dir)
                shutil.copy2(os.path.join(temp_dir, "LightInDark.dll"), plugins_dir)
                log_message("文件复制成功")
                shutil.rmtree(temp_dir, ignore_errors=True)
                show_info_message("更新完成！")
                progress_win.root.after(100, progress_win.root.quit)
                print("updated")
            except Exception as e:
                log_message(f"复制失败: {e}")
                show_error_message(f"文件复制失败: {e}")
                print("copy failed")
                progress_win.root.after(100, progress_win.root.quit)
        else:
            retry = ctypes.windll.user32.MessageBoxW(
                0,
                f"下载失败 ({', '.join(failed)})，是否尝试使用镜像源？",
                "下载失败",
                0x00000004 | 0x00000020
            )
            if retry == 6:
                ok2, _ = download_files_with_progress(all_assets, temp_dir, progress_win, mirror=True)
                if ok2:
                    try:
                        shutil.copy2(os.path.join(temp_dir, "Light.dll"), plugins_dir)
                        shutil.copy2(os.path.join(temp_dir, "LightInDark.dll"), plugins_dir)
                        log_message("文件复制成功（镜像）")
                        shutil.rmtree(temp_dir, ignore_errors=True)
                        show_info_message("更新完成！")
                        progress_win.root.after(100, progress_win.root.quit)
                        print("updated")
                    except Exception as e:
                        log_message(f"复制失败（镜像）: {e}")
                        show_error_message(f"文件复制失败: {e}")
                        print("copy failed")
                        progress_win.root.after(100, progress_win.root.quit)
                else:
                    log_message("镜像下载也失败")
                    show_error_message("下载失败，请检查网络后重试。")
                    print("download failed")
                    progress_win.root.after(100, progress_win.root.quit)
            else:
                log_message("用户取消镜像重试")
                show_error_message("更新已取消。")
                print("canceled")
                progress_win.root.after(100, progress_win.root.quit)

    threading.Thread(target=download_thread, daemon=True).start()
    progress_win.run()

def quick_check():
    program_dir = get_program_dir()
    bepinex_dir = find_bepinex(program_dir)
    if bepinex_dir is None:
        print("not installed")
        return

    plugins_dir = os.path.join(bepinex_dir, "plugins")
    if not os.path.isdir(plugins_dir):
        print("not installed")
        return

    dll1 = os.path.join(plugins_dir, "Light.dll")
    dll2 = os.path.join(plugins_dir, "LightInDark.dll")
    if not (os.path.isfile(dll1) and os.path.isfile(dll2)):
        print("not installed")
        return

    light_ver, lid_ver = get_local_versions(plugins_dir)
    if light_ver is None or lid_ver is None:
        print("no need")
        return

    remote_light = get_remote_version(REPO_LIGHT_OWNER, REPO_LIGHT_NAME)
    remote_lid = get_remote_version(REPO_LID_OWNER, REPO_LID_NAME)

    if remote_light is None or remote_lid is None:
        print("github error")
        return

    try:
        need_update = (version_compare(light_ver, remote_light) < 0) or \
                      (version_compare(lid_ver, remote_lid) < 0)
    except:
        need_update = True

    print("need update" if need_update else "no need")
def main():
    if len(sys.argv) > 1 and sys.argv[1] == "--listen":
        listen_and_update()
    else:
        quick_check()

if __name__ == "__main__":
    main()