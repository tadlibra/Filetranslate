"""
Minecraft Mod Translator - App Windows
Giao diện hiện đại dùng CustomTkinter
"""

import customtkinter as ctk
import tkinter as tk
from tkinter import filedialog
import threading
import zipfile
import json
import os
import time
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor, as_completed

ctk.set_appearance_mode("dark")
ctk.set_default_color_theme("blue")

LANG_OPTIONS = {
    "Tiếng Việt": ("vi", "vi_vn"),
    "Tiếng Trung": ("zh-CN", "zh_cn"),
    "Tiếng Nhật": ("ja", "ja_jp"),
    "Tiếng Hàn": ("ko", "ko_kr"),
    "Tiếng Thái": ("th", "th_th"),
    "Tiếng Indonesia": ("id", "id_id"),
}


def find_curseforge_path():
    """Auto-detect CurseForge Instances folder."""
    candidates = [
        Path.home() / "curseforge" / "minecraft" / "Instances",
        Path.home() / "AppData" / "Roaming" / "CurseForge" / "minecraft" / "Instances",
        Path("C:/curseforge/minecraft/Instances"),
        Path("D:/curseforge/minecraft/Instances"),
    ]
    for p in candidates:
        if p.exists():
            return str(p)
    return str(Path.home())


class App(ctk.CTk):
    def __init__(self):
        super().__init__()
        self.title("Minecraft Mod Translator")
        self.geometry("780x620")
        self.resizable(False, False)
        self.configure(fg_color="#0f1117")

        self.running = False
        self.total_mods = 0
        self.done_mods = 0
        self._curseforge_path = find_curseforge_path()

        self._build_ui()

    def _build_ui(self):
        # HEADER
        header = ctk.CTkFrame(self, fg_color="#161b27", corner_radius=0, height=64)
        header.pack(fill="x")
        header.pack_propagate(False)

        ctk.CTkLabel(
            header, text="⛏  Minecraft Mod Translator",
            font=ctk.CTkFont(family="Segoe UI", size=20, weight="bold"),
            text_color="#4fc3f7"
        ).pack(side="left", padx=24, pady=16)

        ctk.CTkLabel(
            header, text="Dịch ngôn ngữ cho mọi modpack",
            font=ctk.CTkFont(size=12),
            text_color="#546e7a"
        ).pack(side="left", pady=16)

        # MAIN CONTENT
        main = ctk.CTkFrame(self, fg_color="transparent")
        main.pack(fill="both", expand=True, padx=20, pady=16)

        left = ctk.CTkFrame(main, fg_color="transparent", width=360)
        left.pack(side="left", fill="both", padx=(0, 10))
        left.pack_propagate(False)

        right = ctk.CTkFrame(main, fg_color="transparent")
        right.pack(side="left", fill="both", expand=True)

        # CỘT TRÁI
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

        self._section(left, "🔧  Dịch vụ dịch thuật")
        self.service_var = ctk.StringVar(value="Google Translate (miễn phí)")
        ctk.CTkOptionMenu(
            left,
            values=["Google Translate (miễn phí)", "Claude AI (chất lượng cao)"],
            variable=self.service_var,
            fg_color="#1a1f2e", button_color="#1e3a5f",
            button_hover_color="#1565c0",
            font=ctk.CTkFont(size=13), height=38,
            command=self.toggle_api_key
        ).pack(fill="x", pady=(0, 8))

        # API key frame — auto-fill từ env var
        self.api_frame = ctk.CTkFrame(left, fg_color="transparent")
        api_label_frame = ctk.CTkFrame(self.api_frame, fg_color="transparent")
        api_label_frame.pack(fill="x")
        ctk.CTkLabel(api_label_frame, text="API Key", font=ctk.CTkFont(size=12), text_color="#546e7a").pack(side="left")
        env_key = os.environ.get("ANTHROPIC_API_KEY", "")
        if env_key:
            ctk.CTkLabel(
                api_label_frame, text="✓ loaded từ môi trường",
                font=ctk.CTkFont(size=11), text_color="#4caf50"
            ).pack(side="left", padx=(8, 0))
        self.api_var = tk.StringVar(value=env_key)
        ctk.CTkEntry(
            self.api_frame, textvariable=self.api_var,
            placeholder_text="sk-ant-...", show="*",
            fg_color="#1a1f2e", border_color="#2a3a4a",
            font=ctk.CTkFont(size=12), height=36
        ).pack(fill="x", pady=(4, 0))

        self._section(left, "⚙️  Cài đặt nâng cao")
        adv = ctk.CTkFrame(left, fg_color="transparent")
        adv.pack(fill="x", pady=(0, 12))

        ctk.CTkLabel(adv, text="Mod song song:", font=ctk.CTkFont(size=12), text_color="#78909c").pack(side="left")
        self.workers_var = tk.StringVar(value="5")
        ctk.CTkEntry(adv, textvariable=self.workers_var, width=50, height=30, fg_color="#1a1f2e", border_color="#2a3a4a", font=ctk.CTkFont(size=12)).pack(side="left", padx=(8, 16))
        ctk.CTkLabel(adv, text="Batch size:", font=ctk.CTkFont(size=12), text_color="#78909c").pack(side="left")
        self.batch_var = tk.StringVar(value="50")
        ctk.CTkEntry(adv, textvariable=self.batch_var, width=50, height=30, fg_color="#1a1f2e", border_color="#2a3a4a", font=ctk.CTkFont(size=12)).pack(side="left", padx=8)

        self.start_btn = ctk.CTkButton(
            left, text="▶  BẮT ĐẦU DỊCH", height=46,
            fg_color="#1565c0", hover_color="#0d47a1",
            font=ctk.CTkFont(size=15, weight="bold"),
            corner_radius=10,
            command=self.start_translate
        )
        self.start_btn.pack(fill="x", pady=(8, 0))

        self.stop_btn = ctk.CTkButton(
            left, text="⏹  DỪNG LẠI", height=38,
            fg_color="#b71c1c", hover_color="#7f0000",
            font=ctk.CTkFont(size=13),
            corner_radius=10,
            command=self.stop_translate,
            state="disabled"
        )
        self.stop_btn.pack(fill="x", pady=(6, 0))

        # CỘT PHẢI
        self._section(right, "📊  Tiến trình")

        prog_frame = ctk.CTkFrame(right, fg_color="#1a1f2e", corner_radius=10)
        prog_frame.pack(fill="x", pady=(0, 10))

        stats = ctk.CTkFrame(prog_frame, fg_color="transparent")
        stats.pack(fill="x", padx=12, pady=(10, 4))

        self.mod_label = ctk.CTkLabel(stats, text="0 / 0 mod", font=ctk.CTkFont(size=13, weight="bold"), text_color="#4fc3f7")
        self.mod_label.pack(side="left")
        self.pct_label = ctk.CTkLabel(stats, text="0%", font=ctk.CTkFont(size=13, weight="bold"), text_color="#4fc3f7")
        self.pct_label.pack(side="right")

        self.progress_bar = ctk.CTkProgressBar(prog_frame, height=10, fg_color="#0d1b2a", progress_color="#1565c0", corner_radius=5)
        self.progress_bar.pack(fill="x", padx=12, pady=(0, 10))
        self.progress_bar.set(0)

        self.status_label = ctk.CTkLabel(right, text="Sẵn sàng...", font=ctk.CTkFont(size=12), text_color="#546e7a")
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

        # Startup messages
        cf = self._curseforge_path
        if cf != str(Path.home()):
            self.log(f"✓ CurseForge instances: {cf}")
        else:
            self.log("⚠ Không tìm thấy CurseForge, hãy Browse chọn thư mục instance thủ công.")
        if env_key:
            self.log("✓ Claude API Key đã load từ biến môi trường ANTHROPIC_API_KEY.")
        self.log("App sẵn sàng! Chọn thư mục instance và nhấn Bắt đầu dịch.")

    def _section(self, parent, text):
        ctk.CTkLabel(parent, text=text, font=ctk.CTkFont(size=12, weight="bold"), text_color="#78909c").pack(anchor="w", pady=(8, 4))

    def toggle_api_key(self, val):
        if "Claude" in val:
            self.api_frame.pack(fill="x", pady=(0, 8))
        else:
            self.api_frame.pack_forget()

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

    def start_translate(self):
        path = self.path_var.get().strip()
        if not path:
            self.log("⚠ Vui lòng chọn thư mục instance!")
            return
        mods_path = os.path.join(path, "mods")
        if not os.path.exists(mods_path):
            self.log(f"⚠ Không tìm thấy thư mục mods/ trong: {path}")
            return

        try:
            workers = int(self.workers_var.get() or 5)
            if not (1 <= workers <= 32):
                self.log("⚠ Số mod song song phải từ 1–32, dùng mặc định 5")
                workers = 5
        except ValueError:
            self.log("⚠ Số mod song song không hợp lệ, dùng mặc định 5")
            workers = 5

        try:
            batch_size = int(self.batch_var.get() or 50)
            if not (1 <= batch_size <= 200):
                self.log("⚠ Batch size phải từ 1–200, dùng mặc định 50")
                batch_size = 50
        except ValueError:
            self.log("⚠ Batch size không hợp lệ, dùng mặc định 50")
            batch_size = 50

        if "Claude" in self.service_var.get():
            api_key = self.api_var.get().strip()
            if not api_key:
                self.log("⚠ Vui lòng nhập Claude API Key!")
                return
            if not api_key.startswith("sk-ant-"):
                self.log("⚠ API Key không hợp lệ (phải bắt đầu bằng sk-ant-)")
                return

        self.running = True
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
            pack_name = "VietNamese_Complete"
            output_pack = os.path.join(path, "resourcepacks", pack_name)
            lang_name = self.lang_var.get()
            lang_code, lang_file = LANG_OPTIONS.get(lang_name, ("vi", "vi_vn"))
            service = self.service_var.get()
            api_key = self.api_var.get().strip()

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
            self.log(f"✓ Ngôn ngữ: {lang_name} | Dịch vụ: {'Claude AI' if 'Claude' in service else 'Google Translate'}")
            self.log(f"✓ {workers} mod song song | Batch {batch_size} key/lần\n")
            self._set_status("Đang dịch...")

            def translate_batch_google(texts):
                # Use null-byte delimited separator to avoid collision with game text
                sep = "\x00||||\x00"
                try:
                    from deep_translator import GoogleTranslator
                    joined = sep.join(texts)
                    result = GoogleTranslator(source="en", target=lang_code).translate(joined)
                    if not result:
                        return texts
                    parts = [p.strip() for p in result.split("||||")]
                    if len(parts) == len(texts):
                        return parts
                    # Fallback: translate one-by-one
                    self.log(f"  ⚠ Google Translate: nhận {len(parts)}/{len(texts)} kết quả, thử từng câu")
                    out = []
                    for t in texts:
                        try:
                            r = GoogleTranslator(source="en", target=lang_code).translate(t)
                            out.append(r if r else t)
                            time.sleep(0.15)
                        except Exception:
                            out.append(t)
                    return out
                except Exception as e:
                    self.log(f"  ⚠ Google Translate lỗi: {e}")
                    time.sleep(1)
                    return texts

            def translate_batch_claude(texts):
                try:
                    import anthropic
                    client = anthropic.Anthropic(api_key=api_key)
                    prompt = (
                        f"Translate these Minecraft game strings from English to {lang_name}. "
                        f"Return ONLY a JSON array of translated strings, same order, same count, no explanation:\n"
                        f"{json.dumps(texts, ensure_ascii=False)}"
                    )
                    msg = client.messages.create(
                        model="claude-haiku-4-5-20251001",
                        max_tokens=4096,
                        messages=[{"role": "user", "content": prompt}]
                    )
                    if not msg.content or not hasattr(msg.content[0], "text"):
                        self.log("  ⚠ Claude API: response không hợp lệ")
                        return texts
                    result = json.loads(msg.content[0].text)
                    if not isinstance(result, list):
                        self.log("  ⚠ Claude API: kết quả không phải JSON array")
                        return texts
                    if len(result) != len(texts):
                        self.log(f"  ⚠ Claude API: nhận {len(result)}/{len(texts)} kết quả, tự điều chỉnh")
                        # Pad with originals or truncate to match
                        result = (result + texts)[:len(texts)]
                    return result
                except json.JSONDecodeError as e:
                    self.log(f"  ⚠ Claude API lỗi parse JSON: {e}")
                    return texts
                except Exception as e:
                    self.log(f"  ⚠ Claude API lỗi: {e}")
                    return texts

            translate_fn = translate_batch_claude if "Claude" in service else translate_batch_google

            def process_jar(jar_path):
                if not self.running:
                    return Path(jar_path).stem, []
                mod_name = Path(jar_path).stem
                results = []
                try:
                    with zipfile.ZipFile(jar_path, "r") as zf:
                        entries = zf.namelist()
                        for en_path in [e for e in entries if e.endswith("lang/en_us.json")]:
                            parts = en_path.split("/")
                            if len(parts) < 3:
                                continue
                            namespace = parts[1]
                            out_file = os.path.join(output_pack, "assets", namespace, "lang", f"{lang_file}.json")
                            if os.path.exists(out_file):
                                results.append(f"  ↷ Bỏ qua: {namespace}")
                                continue
                            try:
                                en_json = json.loads(zf.read(en_path).decode("utf-8"))
                            except (json.JSONDecodeError, UnicodeDecodeError) as e:
                                results.append(f"  ✗ Lỗi đọc en_us.json ({namespace}): {e}")
                                continue
                            vi_path = en_path.replace("en_us.json", f"{lang_file}.json")
                            vi_json = {}
                            if vi_path in entries:
                                try:
                                    vi_json = json.loads(zf.read(vi_path).decode("utf-8"))
                                except (json.JSONDecodeError, UnicodeDecodeError):
                                    vi_json = {}
                            missing = {k: v for k, v in en_json.items() if k not in vi_json and isinstance(v, str) and v.strip()}
                            if not missing:
                                continue
                            new_trans = dict(vi_json)
                            keys, vals = list(missing.keys()), list(missing.values())
                            for i in range(0, len(vals), batch_size):
                                if not self.running:
                                    break
                                batch_keys = keys[i:i + batch_size]
                                batch_vals = vals[i:i + batch_size]
                                translated = translate_fn(batch_vals)
                                if len(translated) != len(batch_keys):
                                    self.log(f"  ⚠ Số kết quả không khớp batch ({len(translated)}/{len(batch_keys)}), bỏ qua batch này")
                                    continue
                                for k, v in zip(batch_keys, translated):
                                    new_trans[k] = v
                                time.sleep(0.3)
                            os.makedirs(os.path.dirname(out_file), exist_ok=True)
                            with open(out_file, "w", encoding="utf-8") as f:
                                json.dump(new_trans, f, ensure_ascii=False, indent=2)
                            results.append(f"  ✓ {namespace}: {len(missing)} key đã dịch")
                except Exception as e:
                    results.append(f"  ✗ Lỗi: {e}")
                return mod_name, results

            with ThreadPoolExecutor(max_workers=workers) as ex:
                futures = {ex.submit(process_jar, str(j)): j for j in jar_files}
                for future in as_completed(futures):
                    mod_name, results = future.result()
                    self.done_mods += 1
                    self.update_progress()
                    if any("✓" in r for r in results):
                        self.log(f"[{self.done_mods}/{self.total_mods}] {mod_name}")
                        for r in results:
                            self.log(r)
                    else:
                        self._set_status(f"[{self.done_mods}/{self.total_mods}] {mod_name}")

            if self.running:
                self.log(f"\n✅ HOÀN TẤT! Resource Pack lưu tại:\n{output_pack}")
                self.log("\nBước tiếp theo:")
                self.log("1. Mở Minecraft → Options → Resource Packs")
                self.log("2. Bật 'VietNamese_Complete' lên trên cùng")
                self.log("3. Đổi ngôn ngữ trong Settings")
                self._set_status("✅ Hoàn tất!")
            else:
                self.log("\n⏹ Đã dừng. Chạy lại để tiếp tục từ chỗ dở.")
                self._set_status("Đã dừng")

        except Exception as e:
            self.log(f"\n✗ Lỗi không mong đợi: {e}")
            self._set_status("Lỗi!")
        finally:
            self.running = False
            self.after(0, lambda: self.start_btn.configure(state="normal"))
            self.after(0, lambda: self.stop_btn.configure(state="disabled"))


if __name__ == "__main__":
    app = App()
    app.mainloop()
