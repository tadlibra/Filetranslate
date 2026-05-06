# -*- coding: utf-8 -*-
"""
Minecraft Mod Translator - App Windows
Giao dien hien dai dung CustomTkinter
"""

import customtkinter as ctk
import tkinter as tk
from tkinter import filedialog
import threading
import zipfile
import json
import re
import time
import os
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor, as_completed

ctk.set_appearance_mode("dark")
ctk.set_default_color_theme("blue")

LANG_OPTIONS = {
    "Tiếng Việt": ("vi", "vi_vn"),
}


def find_curseforge_path():
    candidates = [
        Path.home() / "minecraft" / "Instances",
        Path.home() / "AppData" / "minecraft" / "Instances",
        Path("D:/minecraft/Instances"),
        Path("C:/minecraft/Instances"),
    ]
    for p in candidates:
        if p.exists():
            return str(p)
    return str(Path.home())


class App(ctk.CTk):
    def __init__(self):
        super().__init__()
        self.title("Beeslater")
        try:
            self.iconbitmap("icon.ico")
        except:
            pass
        self.geometry("780x620")
        self.resizable(False, False)
        self.configure(fg_color="#0f1117")

        self.running = False
        self.total_mods = 0
        self.done_mods = 0
        self._error_count = 0
        self._curseforge_path = find_curseforge_path()

        self._build_ui()

    def _build_ui(self):
        header = ctk.CTkFrame(self, fg_color="#161b27", corner_radius=0, height=64)
        header.pack(fill="x")
        header.pack_propagate(False)

        ctk.CTkLabel(
            header, text="🐝  Beeslater",
            font=ctk.CTkFont(family="Segoe UI", size=20, weight="bold"),
            text_color="#4fc3f7"
        ).pack(side="left", padx=24, pady=16)

        main = ctk.CTkFrame(self, fg_color="transparent")
        main.pack(fill="both", expand=True, padx=20, pady=16)

        left = ctk.CTkFrame(main, fg_color="transparent", width=360)
        left.pack(side="left", fill="both", padx=(0, 10))
        left.pack_propagate(False)

        right = ctk.CTkFrame(main, fg_color="transparent")
        right.pack(side="left", fill="both", expand=True)

        # COT TRAI
        self._section(left, "📁  Thư mục modpack")
        path_frame = ctk.CTkFrame(left, fg_color="#1a1f2e", corner_radius=8)
        path_frame.pack(fill="x", pady=(0, 12))

        self.path_var = tk.StringVar()
        ctk.CTkEntry(
            path_frame, textvariable=self.path_var,
            placeholder_text="Chọn instance CurseForge...",
            fg_color="#1a1f2e", border_color="#2a3a4a",
            font=ctk.CTkFont(size=12), height=36
        ).pack(side="left", fill="x", expand=True, padx=(8, 4), pady=8)

        ctk.CTkButton(
            path_frame, text="Browse", width=72, height=36,
            fg_color="#1e3a5f", hover_color="#1565c0",
            font=ctk.CTkFont(size=12),
            command=self.browse_folder
        ).pack(side="right", padx=(0, 8), pady=8)

        self._section(left, "🌐  Ngôn ngữ đích")
        self.lang_var = ctk.StringVar(value="Tiếng Việt")
        ctk.CTkOptionMenu(
            left, values=list(LANG_OPTIONS.keys()),
            variable=self.lang_var,
            fg_color="#1a1f2e", button_color="#1e3a5f",
            button_hover_color="#1565c0",
            font=ctk.CTkFont(size=13), height=38
        ).pack(fill="x", pady=(0, 12))

        self._section(left, "⚙️  Cài đặt nâng cao")
        adv = ctk.CTkFrame(left, fg_color="transparent")
        adv.pack(fill="x", pady=(0, 12))

        ctk.CTkLabel(adv, text="Mod song song:", font=ctk.CTkFont(size=12), text_color="#78909c").pack(side="left")
        self.workers_var = tk.StringVar(value="5")
        ctk.CTkEntry(adv, textvariable=self.workers_var, width=50, height=30,
            fg_color="#1a1f2e", border_color="#2a3a4a",
            font=ctk.CTkFont(size=12)).pack(side="left", padx=(8, 16))

        ctk.CTkLabel(adv, text="Batch size:", font=ctk.CTkFont(size=12), text_color="#78909c").pack(side="left")
        self.batch_var = tk.StringVar(value="50")
        ctk.CTkEntry(adv, textvariable=self.batch_var, width=50, height=30,
            fg_color="#1a1f2e", border_color="#2a3a4a",
            font=ctk.CTkFont(size=12)).pack(side="left", padx=8)

        self.reset_var = ctk.BooleanVar(value=False)
        ctk.CTkCheckBox(
            left, text="Xóa sạch và dịch lại từ đầu",
            variable=self.reset_var,
            font=ctk.CTkFont(size=12),
            text_color="#ef9a9a",
            fg_color="#b71c1c",
            hover_color="#7f0000",
            checkmark_color="white"
        ).pack(anchor="w", pady=(0, 8))

        self.start_btn = ctk.CTkButton(
            left, text="▶  BẮT ĐẦU DỊCH", height=46,
            fg_color="#1565c0", hover_color="#0d47a1",
            font=ctk.CTkFont(size=15, weight="bold"),
            corner_radius=10,
            command=self.start_translate
        )
        self.start_btn.pack(fill="x", pady=(0, 0))

        self.stop_btn = ctk.CTkButton(
            left, text="⏹  DỪNG LẠI", height=38,
            fg_color="#b71c1c", hover_color="#7f0000",
            font=ctk.CTkFont(size=13),
            corner_radius=10,
            command=self.stop_translate,
            state="disabled"
        )
        self.stop_btn.pack(fill="x", pady=(6, 0))

        # COT PHAI
        self._section(right, "📊  Tiến trình")

        prog_frame = ctk.CTkFrame(right, fg_color="#1a1f2e", corner_radius=10)
        prog_frame.pack(fill="x", pady=(0, 10))

        stats = ctk.CTkFrame(prog_frame, fg_color="transparent")
        stats.pack(fill="x", padx=12, pady=(10, 4))

        self.mod_label = ctk.CTkLabel(stats, text="0 / 0 mod",
            font=ctk.CTkFont(size=13, weight="bold"), text_color="#4fc3f7")
        self.mod_label.pack(side="left")
        self.pct_label = ctk.CTkLabel(stats, text="0%",
            font=ctk.CTkFont(size=13, weight="bold"), text_color="#4fc3f7")
        self.pct_label.pack(side="right")

        self.progress_bar = ctk.CTkProgressBar(prog_frame, height=10,
            fg_color="#0d1b2a", progress_color="#1565c0", corner_radius=5)
        self.progress_bar.pack(fill="x", padx=12, pady=(0, 10))
        self.progress_bar.set(0)

        self.status_label = ctk.CTkLabel(right, text="Sẵn sàng...",
            font=ctk.CTkFont(size=12), text_color="#546e7a")
        self.status_label.pack(anchor="w", pady=(0, 6))

        self._section(right, "📝  Log")
        log_frame = ctk.CTkFrame(right, fg_color="#1a1f2e", corner_radius=10)
        log_frame.pack(fill="both", expand=True)

        self.log_box = ctk.CTkTextbox(
            log_frame, fg_color="#1a1f2e",
            font=ctk.CTkFont(family="Consolas", size=12),
            text_color="#b0bec5",
            corner_radius=10,
            wrap="word"
        )
        self.log_box.pack(fill="both", expand=True, padx=2, pady=2)

        cf = self._curseforge_path
        if cf != str(Path.home()):
            self.log(f"✓ CurseForge instances: {cf}")
        else:
            self.log("⚠ Không tìm thấy CurseForge, hãy Browse chọn thư mục thủ công.")
        self.log("App sẵn sàng! Chọn thư mục instance và nhấn Bắt đầu dịch.")

    def _section(self, parent, text):
        ctk.CTkLabel(parent, text=text,
            font=ctk.CTkFont(size=12, weight="bold"),
            text_color="#78909c").pack(anchor="w", pady=(8, 4))

    def browse_folder(self):
        folder = filedialog.askdirectory(
            title="Chọn thư mục instance (phải chứa thư mục mods/)",
            initialdir=self._curseforge_path
        )
        if folder:
            self.path_var.set(folder)
            self.log(f"Đã chọn: {folder}")

    def log(self, msg):
        def _do():
            self.log_box.configure(state="normal")
            self.log_box.insert("end", msg + "\n")
            self.log_box.see("end")
            self.log_box.configure(state="disabled")
        self.after(0, _do)

    def update_progress(self):
        def _do():
            pct = self.done_mods / max(self.total_mods, 1)
            self.progress_bar.set(pct)
            self.mod_label.configure(text=f"{self.done_mods} / {self.total_mods} mod")
            self.pct_label.configure(text=f"{int(pct * 100)}%")
        self.after(0, _do)

    def _set_status(self, text):
        self.after(0, lambda: self.status_label.configure(text=text))

    def _finish_ui(self):
        self.after(0, lambda: self.start_btn.configure(state="normal"))
        self.after(0, lambda: self.stop_btn.configure(state="disabled"))

    def start_translate(self):
        path = self.path_var.get().strip()
        if not path:
            self.log("⚠ Vui lòng chọn thư mục instance!")
            return
        if not os.path.exists(os.path.join(path, "mods")):
            self.log(f"⚠ Không tìm thấy thư mục mods/ trong: {path}")
            return

        try:
            workers = int(self.workers_var.get() or 5)
            if not (1 <= workers <= 32):
                workers = 5
        except ValueError:
            workers = 5

        try:
            batch_size = int(self.batch_var.get() or 50)
            if not (1 <= batch_size <= 200):
                batch_size = 50
        except ValueError:
            batch_size = 50

        self.running = True
        self._error_count = 0
        self.start_btn.configure(state="disabled")
        self.stop_btn.configure(state="normal")
        self.log_box.configure(state="normal")
        self.log_box.delete("1.0", "end")
        self.log_box.configure(state="disabled")

        threading.Thread(target=self.run_translate, args=(workers, batch_size), daemon=True).start()

    def stop_translate(self):
        self.running = False
        self.log("⏹ Đang dừng...")
        self._set_status("Đang dừng...")

    def run_translate(self, workers, batch_size):
        try:
            path = self.path_var.get().strip()
            mods_path = os.path.join(path, "mods")
            output_pack = os.path.join(path, "resourcepacks", "FileTranslate")
            lang_name = self.lang_var.get()
            lang_code, lang_file = LANG_OPTIONS.get(lang_name, ("vi", "vi_vn"))

            if self.reset_var.get() and os.path.exists(output_pack):
                import shutil
                shutil.rmtree(output_pack)
                self.log("🗑 Đã xóa Resource Pack cũ, dịch lại từ đầu...")
            os.makedirs(output_pack, exist_ok=True)
            with open(os.path.join(output_pack, "pack.mcmeta"), "w") as f:
                json.dump({"pack": {"pack_format": 15, "description": "Auto translated"}}, f)

            jar_files = list(Path(mods_path).glob("*.jar"))
            self.total_mods = len(jar_files)
            self.done_mods = 0

            if self.total_mods == 0:
                self.log("⚠ Không tìm thấy file .jar nào trong thư mục mods/!")
                return

            self.log(f"✓ Tìm thấy {self.total_mods} mod")
            self.log(f"✓ Ngôn ngữ: {lang_name} | Google Translate")
            self.log(f"✓ {workers} mod song song | Batch {batch_size} key/lần\n")
            self._set_status("Đang dịch...")

            from deep_translator import GoogleTranslator

            def translate_batch(texts):
                if not texts:
                    return []
                sep = " ||| "
                try:
                    result = GoogleTranslator(source="en", target=lang_code).translate(sep.join(texts))
                    parts = [p.strip() for p in result.split("|||")]
                    return parts if len(parts) == len(texts) else [
                        GoogleTranslator(source="en", target=lang_code).translate(t) or t for t in texts
                    ]
                except Exception:
                    time.sleep(1)
                    try:
                        return [GoogleTranslator(source="en", target=lang_code).translate(t) or t for t in texts]
                    except Exception:
                        return texts

            def process_jar(jar_path):
                if not self.running:
                    return Path(jar_path).stem, [], 0, True
                mod_name = Path(jar_path).stem
                results = []
                jar_errors = 0
                try:
                    with zipfile.ZipFile(jar_path, "r") as zf:
                        entries = zf.namelist()
                        lang_entries = [e for e in entries if e.endswith("lang/en_us.json")]
                        lang_entries += [e for e in entries if e.endswith("lang/en_us.lang")]

                        for en_path in lang_entries:
                            if not self.running:
                                break
                            parts = en_path.split("/")
                            if len(parts) < 3:
                                continue
                            namespace = parts[1]
                            out_file = os.path.join(output_pack, "assets", namespace, "lang", f"{lang_file}.json")
                            if os.path.exists(out_file):
                                results.append(f"  ↷ Bỏ qua: {namespace}")
                                continue

                            is_lang_file = en_path.endswith(".lang")
                            try:
                                raw = zf.read(en_path).decode("utf-8", errors="replace")
                                cleaned = re.sub(r"[\x00-\x08\x0b\x0c\x0e-\x1f]", "", raw)

                                if is_lang_file:
                                    en_json = {}
                                    for line in raw.splitlines():
                                        line = line.strip()
                                        if not line or line.startswith("#"):
                                            continue
                                        if "=" in line:
                                            k, _, v = line.partition("=")
                                            en_json[k.strip()] = v.strip()
                                else:
                                    try:
                                        en_json = json.loads(cleaned)
                                    except json.JSONDecodeError:
                                        results.append(f"  ✗ JSON lỗi: {namespace}")
                                        jar_errors += 1
                                        continue
                            except Exception as e:
                                results.append(f"  ✗ Lỗi đọc file ({namespace}): {e}")
                                jar_errors += 1
                                continue

                            vi_path = en_path.replace("en_us.json", f"{lang_file}.json").replace("en_us.lang", f"{lang_file}.json")
                            vi_json = {}
                            if vi_path in entries:
                                try:
                                    vi_json = json.loads(zf.read(vi_path).decode("utf-8"))
                                except Exception:
                                    vi_json = {}

                            missing = {k: v for k, v in en_json.items()
                                       if k not in vi_json and isinstance(v, str) and v.strip()}
                            if not missing:
                                continue

                            new_trans = dict(vi_json)
                            keys, vals = list(missing.keys()), list(missing.values())
                            batch_failed = False

                            for i in range(0, len(vals), batch_size):
                                if not self.running:
                                    break
                                try:
                                    translated = translate_batch(vals[i:i + batch_size])
                                except Exception as e:
                                    self.log(f"  ✗ Lỗi dịch ({namespace}): {e}")
                                    jar_errors += 1
                                    batch_failed = True
                                    break
                                if len(translated) == len(keys[i:i + batch_size]):
                                    for k, v in zip(keys[i:i + batch_size], translated):
                                        new_trans[k] = v
                                time.sleep(0.3)

                            if batch_failed or not self.running:
                                continue

                            os.makedirs(os.path.dirname(out_file), exist_ok=True)
                            with open(out_file, "w", encoding="utf-8") as f:
                                json.dump(new_trans, f, ensure_ascii=False, indent=2)
                            results.append(f"  ✓ {namespace}: {len(missing)} key đã dịch")

                except Exception as e:
                    results.append(f"  ✗ Lỗi: {e}")
                    jar_errors += 1

                return mod_name, results, jar_errors, False

            with ThreadPoolExecutor(max_workers=workers) as ex:
                futures = {ex.submit(process_jar, str(j)): j for j in jar_files}
                for future in as_completed(futures):
                    mod_name, results, jar_errors, skipped = future.result()
                    if skipped:
                        continue
                    self._error_count += jar_errors
                    self.done_mods += 1
                    self.update_progress()
                    if any("✓" in r for r in results):
                        self.log(f"[{self.done_mods}/{self.total_mods}] {mod_name}")
                        for r in results:
                            self.log(r)
                    elif jar_errors > 0:
                        self.log(f"[{self.done_mods}/{self.total_mods}] {mod_name} -- CÓ LỖI")
                        for r in results:
                            self.log(r)
                    else:
                        self._set_status(f"[{self.done_mods}/{self.total_mods}] {mod_name}")

            if not self.running:
                self.log("\n⏹ Đã dừng. Chạy lại để tiếp tục từ chỗ dở.")
                self._set_status("Đã dừng")
            elif self._error_count > 0:
                self.log(f"\n⚠ Hoàn tất nhưng có {self._error_count} lỗi.")
                self.log(f"Resource Pack lưu tại:\n{output_pack}")
                self._set_status(f"Hoàn tất - {self._error_count} lỗi")
            else:
                self.log(f"\n✅ HOÀN TẤT! Resource Pack lưu tại:\n{output_pack}")
                self.log("\n1. Mở Minecraft → Options → Resource Packs")
                self.log("2. Bật 'FileTranslate' lên trên cùng")
                self.log("3. Đổi ngôn ngữ trong Settings")
                self._set_status("✅ Hoàn tất!")

        except Exception as e:
            self.log(f"\n✗ Lỗi không mong đợi: {e}")
            self._set_status("Lỗi!")
        finally:
            self.running = False
            self._finish_ui()


if __name__ == "__main__":
    app = App()
    app.mainloop()