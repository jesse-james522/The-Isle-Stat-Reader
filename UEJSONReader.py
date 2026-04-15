#!/usr/bin/env python3
# UEJSONReader.py
# Full, cleaned script — overlays multiple JSON curves in a single pop-up Matplotlib window
# and shows BalanceAttributes / Calculated stats in separate popup windows.
# Place a 'JSONs' folder next to this script or the EXE (when packaged).

import json
import os
import sys
import glob
import re
from typing import Dict, List, Any, Tuple

import tkinter as tk
import tkinter.ttk as ttk
from tkinter import Toplevel, Text, Scrollbar, END, filedialog, messagebox

import matplotlib
matplotlib.use("TkAgg")
from matplotlib.figure import Figure
from matplotlib.backends.backend_tkagg import FigureCanvasTkAgg
from matplotlib.ticker import FuncFormatter
import numpy as np 

# ------------------------- Interpolation Helper -------------------------
def calculate_cubic_segment(t1, p1, m1, t2, p2, m2, num_points=25):
    """Beräknar punkter för ett kubiskt Hermite-segment."""
    t_cubic = np.linspace(t1, t2, num_points)
    delta_t = t2 - t1
    
    # Om segmentet har noll längd, returnera bara startpunkten
    if delta_t == 0 or np.isclose(delta_t, 0.0):
        return np.array([t1]), np.array([p1])
        
    s = (t_cubic - t1) / delta_t
    
    s2 = s * s
    s3 = s2 * s

    # Hermite basfunktioner
    h00 = 2*s3 - 3*s2 + 1
    h10 = (s3 - 2*s2 + s) * delta_t
    h01 = -2*s3 + 3*s2
    h11 = (s3 - s2) * delta_t
    
    # Beräkna de interpolerade värdena
    value_cubic = h00*p1 + h01*p2 + h10*m1 + h11*m2
    return t_cubic, value_cubic

# ------------------------- EXE-aware path helpers -------------------------
def get_app_dir() -> str:
    """Return the folder the app is running from (EXE dir when frozen, script dir otherwise)."""
    if getattr(sys, "frozen", False):
        return os.path.dirname(sys.executable)
    return os.path.dirname(os.path.abspath(__file__))

def find_jsons_folder_next_to_app() -> str:
    """Return path to <app_dir>/JSONs if it exists, else empty string."""
    p = os.path.join(get_app_dir(), "JSONs")
    return p if os.path.isdir(p) else ""

# ------------------------- DataLoader -------------------------
class DataLoader:
    """Handles all data loading, caching, and processing."""

    def __init__(self, root_dir: str):
        self.root_dir = root_dir
        self._data_cache: Dict[str, Any] = {}

    def _get_json_data(self, file_path: str) -> Any:
        """Safely loads JSON data from a file, using a cache."""
        if not file_path:
            return None
        if file_path in self._data_cache:
            return self._data_cache[file_path]
        try:
            with open(file_path, 'r', encoding='utf-8') as f:
                data = json.load(f)
                self._data_cache[file_path] = data
                return data
        except FileNotFoundError:
            print(f"Error: The file {file_path} was not found.")
            return None
        except json.JSONDecodeError:
            print(f"Error: Invalid JSON format in {file_path}.")
            return None

    def find_dinosaurs(self) -> List[str]:
        """Finds all dinosaur folders in the root directory."""
        if not os.path.isdir(self.root_dir):
            return []
        return sorted([d for d in os.listdir(self.root_dir) if os.path.isdir(os.path.join(self.root_dir, d))])

    def find_plottable_files(self, dinosaur_name: str) -> Dict[str, str]:
        """
        Finds all plottable JSON files for a given dinosaur, excluding
        flat or linear curves.
        """
        files: Dict[str, str] = {}
        attributes_path = os.path.join(self.root_dir, dinosaur_name, "Attributes")

        # Add BalanceAttributes file if it exists
        balance_file_path = os.path.join(self.root_dir, dinosaur_name, f"DT_{dinosaur_name}BalanceAttributes.json")
        if os.path.exists(balance_file_path):
            files[self._format_file_name(balance_file_path, dinosaur_name)] = balance_file_path

        if os.path.isdir(attributes_path):
            for file_path in glob.glob(os.path.join(attributes_path, "ATT_*.json")):
                if not self._is_linear_or_flat(file_path):
                    files[self._format_file_name(file_path, dinosaur_name)] = file_path
        return files

    def generate_virtual_attack_files(self, dinosaur_name: str) -> Dict[str, Dict[str, Any]]:
        """Generates virtual JSON data for attack graphs."""
        virtual_data: Dict[str, Dict[str, Any]] = {}
        balance_file_path = os.path.join(self.root_dir, dinosaur_name, f"DT_{dinosaur_name}BalanceAttributes.json")
        attack_power_file_path = os.path.join(self.root_dir, dinosaur_name, "Attributes", f"ATT_{dinosaur_name}_AttackPower.json")

        balance_data = self._get_json_data(balance_file_path)
        attack_power_data = self._get_json_data(attack_power_file_path)

        if not balance_data or not attack_power_data:
            return {}

        try:
            ap_item = attack_power_data[0] if isinstance(attack_power_data, list) and attack_power_data else attack_power_data
            ap_curves = None
            if isinstance(ap_item, dict):
                ap_curves = ap_item.get("FloatCurves") or [ap_item.get("Properties", {}).get("FloatCurve")]
            if not ap_curves:
                return {}
        except Exception:
            return {}

        try:
            rows_obj = balance_data[0].get("Rows") if isinstance(balance_data, list) and balance_data else (balance_data.get("Rows", {}) if isinstance(balance_data, dict) else {})
            if not rows_obj:
                return {}
        except Exception:
            return {}

        damage_attributes = {}
        for k, v in rows_obj.items():
            try:
                if isinstance(v, dict) and k.startswith("Damage."):
                    val = v.get("AttributePercentageValues") or v.get("AttributePercentageValue") or v.get("Value")
                    if val not in (None, 0):
                        damage_attributes[k] = val
            except Exception:
                continue

        for attack_name, damage_value in damage_attributes.items():
            clean_attack_name = attack_name.split('.')[-1]
            formatted_name = self._format_virtual_name(clean_attack_name, dinosaur_name)
            display_name = f"{formatted_name} Attack"

            new_curves = []
            
            if len(ap_curves) >= 2:
                time_points_list, values_list = self._process_dual_curves(
                    ap_curves[:2], float(damage_value)
                )
            else:
                # Fallback för enstaka kurvor
                time_points_list, values_list = self._process_generic_curves(
                    ap_curves[:1], float(damage_value)
                )

            for time_points, values in zip(time_points_list, values_list):
                 if time_points and values:
                    new_curves.append({"Time": time_points, "Values": values})


            if new_curves:
                virtual_data[display_name] = {
                    "curves": new_curves,
                    "y_label": "Damage",
                    "title_name": display_name
                }
        return virtual_data

    def generate_calculated_stats(self, dinosaur_name: str) -> Dict[str, float]:
        """Calculates survival times based on absolute decay rates: 100 / |Decay Value|."""
        calculated_stats: Dict[str, float] = {}
        balance_file_path = os.path.join(self.root_dir, dinosaur_name, f"DT_{dinosaur_name}BalanceAttributes.json")
        balance_data = self._get_json_data(balance_file_path)

        if not balance_data:
            return {}

        # These aquatic/semi-aquatic dinos have a special dehydration mechanic:
        # their effective thirst drain is ~66.7% of the bar, so multiply by 2/3.
        AQUATIC_DINOS = {"Beipiaosaurus", "Deinosuchus"}
        thirst_correction = (2 / 3) if dinosaur_name in AQUATIC_DINOS else 1.0

        try:
            rows = balance_data[0].get("Rows", {}) if isinstance(balance_data, list) and balance_data else (balance_data.get("Rows", {}) if isinstance(balance_data, dict) else {})

            for key, row_val in rows.items():
                if not isinstance(row_val, dict):
                    continue
                value = row_val.get("AttributePercentageValues") or row_val.get("AttributePercentageValue") or row_val.get("Value")
                if value is None or value == 0:
                    continue

                if key == "Hunger.Decay":
                    calculated_stats["Time to Starve (min)"] = (100 / abs(value)) * thirst_correction
                elif key == "Thirst.Decay":
                    calculated_stats["Time to Dehydrate (min)"] = (100 / abs(value)) * thirst_correction
                elif key == "Oxygen.Decay":
                    calculated_stats["Time Underwater (min)"] = 100 / abs(value)
                elif key == "Stamina.Spending.Sprinting":
                    calculated_stats["Sprint Duration (min)"] = 100 / abs(value)

        except Exception:
            return {}
        return calculated_stats
     
    def _get_interpolated_curve(self, keys, conversion_factor):
        if not keys:
            return [], []

        time_points, values = self._interpolate_keys(keys, conversion_factor)

        if not time_points:
            return [], []

        # Constant pre-extrapolation to t=0 (mirrors UE's PreInfinityExtrap=RCCE_Constant)
        if time_points[0] > 0.0:
            time_points.insert(0, 0.0)
            values.insert(0, values[0])

        # Clamp lower bound to 0 (no negative times)
        time_points = [float(max(t, 0.0)) for t in time_points]

        return time_points, values

    def _process_dual_curves(self, curve_data_list, conversion_factor):
        """Processes base + elder curves:
           - Base (senior): shown from 0 to 0.75, truncated at 0.75
           - Elder: shown from 0.75 to 1.0, extended to 1.0 if needed
        """

        if len(curve_data_list) < 2:
            return self._process_generic_curves(curve_data_list, conversion_factor)

        time_points_list = []
        values_list = []

        # -------------------------------------------------
        # --------------------- BASE (SENIOR) --------------
        # -------------------------------------------------
        senior = curve_data_list[0].get("Keys", [])
        t_s, v_s = self._get_interpolated_curve(senior, conversion_factor)

        if t_s:
            ts = np.array(t_s, dtype=float)
            vs = np.array(v_s, dtype=float)

            # Senior shows its full range (0 → 1.0+), no truncation at 0.75
            time_points_list.append(list(ts))
            values_list.append(list(vs))

        # -------------------------------------------------
        # --------------------- ELDER ----------------------
        # -------------------------------------------------
        elder = curve_data_list[1].get("Keys", [])
        t_e, v_e = self._get_interpolated_curve(elder, conversion_factor)

        if t_e:
            te = np.array(t_e, dtype=float)
            ve = np.array(v_e, dtype=float)

            # ---- Extend elder to 1.0 if needed ----
            if te[-1] < 1.0:
                te = np.append(te, 1.0)
                ve = np.append(ve, ve[-1])

            # --- Mask region before 0.75 so elder shows 0.75 → 1.0 ---
            mask = te < 0.75
            ve[mask] = np.nan

            time_points_list.append(list(te))
            values_list.append(list(ve))

        return time_points_list, values_list

    def _interpolate_keys(self, keys: List[Dict[str, Any]], conversion_factor: float) -> Tuple[List[float], List[float]]:
        """Generic function to interpolate a single curve's keys."""
        if not keys: return [], []

        processed_time_points = []
        processed_values = []

        # Lägg till första punkten manuellt
        key_first = keys[0]
        t_first = float(key_first.get("Time", 0.0))
        v_first = float(key_first.get("Value", 0.0))
        
        processed_time_points.append(t_first)
        processed_values.append(v_first * conversion_factor)

        # Loopa över alla segment
        for i in range(len(keys) - 1):
            key1 = keys[i]
            key2 = keys[i+1]

            t1 = float(key1.get("Time", 0.0))
            v1_raw = float(key1.get("Value", 0.0))
            m1_raw = float(key1.get("LeaveTangent", 0.0))
            
            t2 = float(key2.get("Time", 0.0))
            v2_raw = float(key2.get("Value", 0.0))
            m2_raw = float(key2.get("ArriveTangent", 0.0))
            
            interp_mode = key1.get("InterpMode", "RCIM_Linear")

            if interp_mode == "RCIM_Cubic":
                t_cubic, v_cubic_raw = calculate_cubic_segment(
                    t1, v1_raw, m1_raw, 
                    t2, v2_raw, m2_raw
                )
                processed_time_points.extend(t_cubic[1:])
                processed_values.extend(list(v_cubic_raw[1:] * conversion_factor))
            else: # RCIM_Linear
                # FIX: Sample linear segment for hover accuracy
                if t2 > t1 and not np.isclose(t2, t1):
                    num_points = 25
                    t_linear = np.linspace(t1, t2, num_points)
                    
                    v1 = v1_raw * conversion_factor
                    v2 = v2_raw * conversion_factor
                    
                    # Calculate linear interpolation: V(s) = V1 + s * (V2 - V1)
                    s = (t_linear - t1) / (t2 - t1)
                    v_linear = v1 + s * (v2 - v1)
                    
                    # Extend lists with points from index 1 (excluding t1, including t2)
                    processed_time_points.extend(t_linear[1:])
                    processed_values.extend(v_linear[1:])
                else:
                    # Tiden är densamma. Lägg till slutpunkten om den inte redan finns
                    if not processed_time_points or not np.isclose(processed_time_points[-1], t2):
                        processed_time_points.append(t2)
                        processed_values.append(v2_raw * conversion_factor)

        return processed_time_points, processed_values

    def _process_generic_curves(self, curve_data_list: List[Dict[str, Any]], conversion_factor: float) -> Tuple[List[List[float]], List[List[float]]]:
        """Processes generic single curves (without time filtering)."""
        time_points_list: List[List[float]] = []
        values_list: List[List[float]] = []

        for curve in curve_data_list:
            keys = curve.get("Keys")
            if not keys or not isinstance(keys, list) or len(keys) == 0:
                continue
            
            # Använd den universella metoden som nu inte tvingar till 1.0
            time_points, values = self._get_interpolated_curve(keys, conversion_factor)
            
            if time_points and values:
                time_points_list.append(time_points)
                values_list.append(values)
        
        return time_points_list, values_list


    def get_plot_data(self, file_path: str, file_name: str) -> Tuple[List[List[float]], List[List[float]], str, str]:
        """Extracts and formats plot data from a file, applying conversions."""
        
        file_data = self._get_json_data(file_path)
        if not file_data:
            return [], [], "", ""

        try:
            item = file_data[0] if isinstance(file_data, list) and file_data else file_data
            # Try to get the list of curves from Properties/FloatCurves
            float_curves = item.get("FloatCurves") or (item.get("Properties", {}).get("FloatCurve") if isinstance(item, dict) else None)
            
            # If FloatCurves is a single dict, wrap it in a list.
            if isinstance(float_curves, dict) and 'Keys' in float_curves:
                 float_curves = [float_curves]

            if not float_curves or not float_curves[0]:
                return [], [], "", ""
        except Exception:
            return [], [], "", ""

        y_label = "Value"
        conversion_factor = 1.0

        lower_file_name = file_name.lower()
        if "speed" in lower_file_name:
            y_label = "Value (km/h)"
            conversion_factor = 0.036
        elif "weight" in lower_file_name:
            y_label = "Value (kg)"

        # --- DUAL CURVE HANDLING (Now with masking) ---
        if len(float_curves) >= 2:
            time_points_list, values_list = self._process_dual_curves(
                float_curves[:2], conversion_factor
            )
        else:
            # --- GENERIC HANDLING (Single Curve) ---
            time_points_list, values_list = self._process_generic_curves(
                float_curves[:1], conversion_factor
            )
        
        return time_points_list, values_list, y_label, file_name

    def _is_linear_or_flat(self, file_path: str) -> bool:
        """Checks if a JSON file represents a linear or flat curve."""
        data = self._get_json_data(file_path)
        if not data:
            return True

        try:
            item = data[0] if isinstance(data, list) and data else data
            float_curves = item.get("FloatCurves") or ([item.get("Properties", {}).get("FloatCurve")] if isinstance(item, dict) else None)
            if not float_curves or not float_curves[0]:
                return True
            keys = float_curves[0].get("Keys") if isinstance(float_curves[0], dict) else None
            if not keys:
                return True

            if len(keys) <= 2:
                return True
            
            valid_keys = [k for k in keys if isinstance(k, dict) and k.get("Time") is not None and k.get("Value") is not None]
            if len(valid_keys) <= 2:
                return True

            time_points = [float(key.get("Time")) for key in valid_keys]
            values = [float(key.get("Value")) for key in valid_keys]

            if all(v == values[0] for v in values):
                return True

            slopes = []
            for i in range(len(valid_keys) - 1):
                time_diff = time_points[i+1] - time_points[i]
                value_diff = values[i+1] - values[i]
                if time_diff == 0:
                    if value_diff != 0:
                        return True 
                    slopes.append(0)
                else:
                    slopes.append(value_diff / time_diff)
            
            if not slopes:
                return True

            first_slope = slopes[0]
            if all(abs(s - first_slope) < 1e-9 for s in slopes):
                return True

        except Exception:
            return True

        return False

    @staticmethod
    def _format_file_name(file_name: str, dinosaur_name: str) -> str:
        """Cleans up and formats the file name for display."""
        name = os.path.basename(file_name).replace(".json", "")
        if name.startswith("ATT_"):
            name = name.replace(f"ATT_{dinosaur_name}_", f"{dinosaur_name}")
        elif name.startswith("DT_"):
            name = name.replace(f"DT_{dinosaur_name}", f"{dinosaur_name}")
        
        name = re.sub(r'([A-Z])', r' \g<1>', name).strip()
        return name

    @staticmethod
    def _format_virtual_name(name: str, dinosaur_name: str) -> str:
        """Cleans up and formats the virtual file name, with dinosaur prefix."""
        formatted = re.sub(r'([A-Z])', r' \g<1>', name).strip()
        return f"{dinosaur_name} {formatted}"

# ------------------------- OverlayPlotter (Tk-embedded) -------------------------
class OverlayPlotter:
    """Keeps multiple Toplevels + Matplotlib Figures and overlays multiple plots."""

    def __init__(self, master: tk.Tk):
        self.master = master
        self.windows: List[Dict] = []  # Track multiple windows
        self._window_counter = 0
        self.target_window_var = tk.StringVar(master, value="New Window")
        self.on_windows_changed = None  # callback: () -> None

        # Variabler för vertikala linjer och X-axelns intervall
        self.show_elder_line = tk.BooleanVar(master, value=True)
        self.show_juvi_line = tk.BooleanVar(master, value=True)
        self.show_subadult_line = tk.BooleanVar(master, value=True)
        # Entry variable för X-axeln 
        self.x_tick_entry_var = tk.StringVar(master, value="0.10") 
        # Entry variable för Y-axeln
        self.y_tick_entry_var = tk.StringVar(master, value="") # Start empty, will be set in add_plot

    def _calculate_initial_y_step(self, y_range: float) -> float:
        """Calculates a sensible initial Y-axis step based on the data range (replicating old logic)."""
        
        if y_range <= 1e-9:
             # Fallback if range is zero or extremely small
             return 0.1 

        if y_range <= 10:
            step = 1.0
        elif y_range <= 100:
            step = 5.0
        elif y_range <= 1000:
            step = 50.0
        else:
            step = 500.0
        
        return step

    def _create_window(self) -> Dict:
        win = Toplevel(self.master)
        win.title("Overlay Plot")
        
        fig = Figure(figsize=(10, 6), dpi=100)
        ax = fig.add_subplot(111)
        ax.set_xlabel("Time")
        ax.grid(True)
        # Vertikala linjer hanteras nu av _update_vertical_lines
        ax.xaxis.set_major_formatter(FuncFormatter(lambda x, pos: f'{x:.2f}' if x > 1.0 else f'{int(x*100)}%'))
        # Initial inställning för X-axeln (10%)
        ax.xaxis.set_major_locator(matplotlib.ticker.MultipleLocator(0.1))
        # Initial inställning för Y-axeln (uppdateras i add_plot)
        ax.yaxis.set_major_locator(matplotlib.ticker.MultipleLocator(1.0)) 

        canvas = FigureCanvasTkAgg(fig, master=win)
        canvas.get_tk_widget().pack(fill='both', expand=True)

        annot = ax.annotate("", xy=(0,0), xytext=(20,20), textcoords='offset points',
                            bbox=dict(boxstyle='round', fc='yellow', alpha=0.8),
                            arrowprops=dict(arrowstyle='->'))
        annot.set_visible(False)

        lines: List[Any] = []
        labels: List[str] = []
        line_vars: List[tk.BooleanVar] = []
        vertical_lines: List[Any] = [] # För de vertikala linjerna
        
        canvas.mpl_connect('motion_notify_event', lambda event: self._on_hover(event, lines, annot))

        # ------------------------- Scrollable Control Frame -------------------------
        control_frame_container = tk.Frame(win) 
        control_frame_container.pack(fill='x', padx=5, pady=5)

        # Statiska kontroller (Borttagningsknapp, Add to Existing)
        static_control_frame = tk.Frame(control_frame_container)
        static_control_frame.pack(fill='x')
        
        tk.Label(static_control_frame, text="Graph Controls:").pack(side='left')
        
        # Lägger till win_dict som parameter till remove_selected för att förenkla logiken
        win_dict_pre_init = {
            'window': win, 'fig': fig, 'ax': ax, 'canvas': canvas,
            'lines': lines, 'labels': labels, 'line_vars': line_vars,
            'vertical_lines': vertical_lines, 'annot': annot
        }
        remove_btn = tk.Button(static_control_frame, text="Remove Selected",
                               command=lambda d=win_dict_pre_init: self._remove_selected(d))
        remove_btn.pack(side='left')

        # Scrollbart område för kryssrutorna (Curve Checkbuttons)
        # Använd en ram för att placera Canvas och Scrollbar korrekt
        curve_list_wrapper = tk.Frame(control_frame_container)
        curve_list_wrapper.pack(fill='x', expand=True)
        
        curve_list_canvas = tk.Canvas(curve_list_wrapper, height=30) 
        curve_list_canvas.pack(side='left', fill='x', expand=True)

        curve_list_scrollbar = tk.Scrollbar(curve_list_wrapper, orient="horizontal", command=curve_list_canvas.xview)
        curve_list_scrollbar.pack(side='bottom', fill='x') # Fyller ut horisontellt

        curve_list_canvas.configure(xscrollcommand=curve_list_scrollbar.set)
        
        # Ram inuti canvas där kryssrutorna placeras
        inner_control_frame = tk.Frame(curve_list_canvas)
        
        # Skapa fönstret inuti Canvas
        curve_list_canvas.create_window((0, 0), window=inner_control_frame, anchor="nw")
        
        # Bind event för att justera rullningsområdet när innehållet ändras
        def on_frame_configure(event):
            curve_list_canvas.configure(scrollregion=curve_list_canvas.bbox("all"))

        inner_control_frame.bind("<Configure>", on_frame_configure)
        # ------------------------- End Scrollable Control Frame -------------------------

        # Fyll i resten av win_dict
        win_dict = win_dict_pre_init
        win_dict['control_frame'] = inner_control_frame # Ram där kryssrutorna för kurvor läggs till
        win_dict['curve_list_canvas'] = curve_list_canvas # Canvas för att uppdatera scrollregion

        # Kontrollram 2: Nya kontroller för vertikala linjer, X-axeln och Y-axeln
        control_frame_2 = tk.Frame(win)
        control_frame_2.pack(fill='x', padx=5, pady=5)
        
        # Vertikala linjer (Tillväxtfaser)
        tk.Label(control_frame_2, text="Growth Lines:").pack(side='left', padx=(5, 0))
        tk.Checkbutton(control_frame_2, text="Elder split (75%)", variable=self.show_elder_line, 
                       command=lambda: self._update_vertical_lines(win_dict)).pack(side='left', padx=2)
        tk.Checkbutton(control_frame_2, text="Subadult (50%)", variable=self.show_subadult_line, 
                       command=lambda: self._update_vertical_lines(win_dict)).pack(side='left', padx=2)
        tk.Checkbutton(control_frame_2, text="Juvi (25%)", variable=self.show_juvi_line, 
                       command=lambda: self._update_vertical_lines(win_dict)).pack(side='left', padx=2)

        # X-Axel Tick-kontroll
        tk.Label(control_frame_2, text="X-Axis Tick Interval (0.01-max):").pack(side='left', padx=(20, 0))
        self.x_tick_entry = tk.Entry(control_frame_2, textvariable=self.x_tick_entry_var, width=5)
        self.x_tick_entry.pack(side='left', padx=2)
        # Bind event till förlorat fokus eller Enter
        self.x_tick_entry.bind('<FocusOut>', lambda e: self._update_x_ticks(win_dict, self.x_tick_entry_var.get()))
        self.x_tick_entry.bind('<Return>', lambda e: self._update_x_ticks(win_dict, self.x_tick_entry_var.get()))
        
        # Y-Axel Tick-kontroll
        tk.Label(control_frame_2, text="Y-Axis Tick Interval (0.01-max):").pack(side='left', padx=(20, 0))
        self.y_tick_entry = tk.Entry(control_frame_2, textvariable=self.y_tick_entry_var, width=5)
        self.y_tick_entry.pack(side='left', padx=2)
        # Bind event
        self.y_tick_entry.bind('<FocusOut>', lambda e: self._update_y_ticks(win_dict, self.y_tick_entry_var.get()))
        self.y_tick_entry.bind('<Return>', lambda e: self._update_y_ticks(win_dict, self.y_tick_entry_var.get()))
        
        # Initial ritning av vertikala linjer
        self._update_vertical_lines(win_dict)


        # Remove window from list when closed
        def on_close():
            if win_dict in self.windows:
                self.windows.remove(win_dict)
            win.destroy()
            self._notify_windows_changed()

        win.protocol("WM_DELETE_WINDOW", on_close)

        self.windows.append(win_dict)
        return win_dict

    def _notify_windows_changed(self):
        if self.on_windows_changed:
            self.on_windows_changed()

    def _update_vertical_lines(self, win_dict: Dict):
        """Draws or removes the Elder, Juvi, and Subadult vertical lines and updates legend."""
        ax = win_dict['ax']
        canvas = win_dict['canvas']
        
        # Ta bort befintliga vertikala linjer
        for line in win_dict['vertical_lines']:
            line.remove()
        win_dict['vertical_lines'].clear()
        
        # Definiera linjerna som potentiellt ska ritas
        line_specs = [
            (0.75, 'r', 'Elder split (75%)', self.show_elder_line.get()),
            (0.50, 'b', 'Subadult (50%)', self.show_subadult_line.get()), 
            (0.25, 'g', 'Juvi (25%)', self.show_juvi_line.get()),
        ]
        
        for x, color, label, is_visible in line_specs:
            if is_visible:
                # Lägg till axvline utan label, Matplotlib hanterar legenden från ax.lines
                line = ax.axvline(x=x, color=color, linestyle='--', label=label)
                win_dict['vertical_lines'].append(line)

        # Uppdatera legenden
        # Endast synliga linjer + vertikala linjer med etikett
        all_visible_lines = [l for l in ax.lines if l.get_visible() and l.get_label() not in ['_nolegend_', '']]
        all_visible_labels = [l.get_label() for l in all_visible_lines]
        
        # Ta bort den gamla legenden
        if ax.get_legend() is not None:
             ax.get_legend().remove()
             
        # Skapa den nya legenden
        if all_visible_lines:
             ax.legend(all_visible_lines, all_visible_labels, loc='upper left', fontsize='small')
        
        canvas.draw_idle()

    def _update_x_ticks(self, win_dict: Dict, interval_text: str):
        """Updates the X-axis major tick locator based on user input."""
        ax = win_dict['ax']
        canvas = win_dict['canvas']
        
        try:
            # Försök att parsa inmatningen som float
            interval = float(interval_text.strip().replace('%', ''))
            
            # Begränsa värdet mellan 0.01 och ett rimligt max
            interval = max(0.01, interval)
            
            # Uppdatera Entry-variabeln med det normaliserade float-värdet
            if interval < 1.0:
                 self.x_tick_entry_var.set(f"{interval:.2f}")
            else:
                 self.x_tick_entry_var.set(f"{interval}")

        except ValueError:
            messagebox.showerror("Input Error", "Please enter a valid number (e.g., 0.05 or 1.0).")
            return
            
        # Safety cap: don't let the user set an interval that generates > 20 ticks
        xmin, xmax = ax.get_xlim()
        x_range = xmax - xmin
        if x_range > 0 and x_range / interval > 20:
            interval = self._calculate_initial_y_step(x_range)
            self.x_tick_entry_var.set(f"{interval}")

        ax.xaxis.set_major_locator(matplotlib.ticker.MultipleLocator(interval))
        canvas.draw_idle()

    # Hanterar Y-axelns uppdatering från inmatning
    def _update_y_ticks(self, win_dict: Dict, interval_text: str):
        """Updates the Y-axis major tick locator based on user input."""
        ax = win_dict['ax']
        canvas = win_dict['canvas']
        
        try:
            # Försök att parsa inmatningen som float
            interval = float(interval_text.strip().replace('%', ''))
            
            # Begränsa värdet till ett positivt värde
            interval = max(0.01, interval)
            
            # Uppdatera Entry-variabeln med det normaliserade float-värdet
            if interval < 1.0:
                 self.y_tick_entry_var.set(f"{interval:.2f}")
            else:
                 self.y_tick_entry_var.set(f"{interval}")

        except ValueError:
            messagebox.showerror("Input Error", "Please enter a valid number (e.g., 5, 0.5, or 50.0).")
            return
            
        ax.yaxis.set_major_locator(matplotlib.ticker.MultipleLocator(interval))
        canvas.draw_idle()

    # Hjälpfunktion för att applicera Y-ticks baserat på entry
    def _apply_y_ticks(self, win_dict: Dict):
        """Applies the Y-axis tick locator based on the current value in the entry field."""
        ax = win_dict['ax']

        try:
            interval = float(self.y_tick_entry_var.get())
            interval = max(0.01, interval)
        except ValueError:
            interval = 1.0

        # Safety cap: never generate more than 200 ticks regardless of user input
        ymin, ymax = ax.get_ylim()
        y_range = ymax - ymin
        if y_range > 0:
            min_safe_interval = y_range / 200.0
            if interval < min_safe_interval:
                interval = self._calculate_initial_y_step(y_range)
                self.y_tick_entry_var.set(f"{interval}")

        ax.yaxis.set_major_locator(matplotlib.ticker.MultipleLocator(interval))
        
    def recompute_y_axis(self, win_dict):
        """Recalculates Y-axis limits + tick interval exactly like add_plot."""
        ax = win_dict['ax']
        canvas = win_dict['canvas']

        # Collect all remaining Y values
        all_vals = []
        for line in win_dict['lines']:
            all_vals.extend([v for v in line.get_ydata() if not np.isnan(v)])

        if not all_vals:
            # fallback
            ax.set_ylim(0, 1)
            self.y_tick_entry_var.set("0.10")
            self._apply_y_ticks(win_dict)
            canvas.draw_idle()
            return

        ymin = min(all_vals)
        ymax = max(all_vals)
        yr   = ymax - ymin

        # identical margin logic to the main algorithm
        if yr > 0:
            y_min = ymin - yr * 0.05
            y_max = ymax + yr * 0.05
        else:
            y_min = ymin * 0.95
            y_max = ymax * 1.05

        ax.set_ylim(y_min, y_max)

        # Compute step exactly like add_plot()
        y_min_m, y_max_m = ax.get_ylim()
        step = self._calculate_initial_y_step(y_max_m - y_min_m)
        self.y_tick_entry_var.set(str(step))

        # Apply the step
        self._apply_y_ticks(win_dict)

        canvas.draw_idle()
        
    def _calculate_initial_y_step(self, y_range: float) -> float:
        """Calculates a sensible initial Y-axis step based on the data range (replicating old logic)."""
        
        if y_range <= 1e-9:
             # Fallback if range is zero or extremely small
             return 0.1 

        if y_range <= 10:
            step = 1.0
        elif y_range <= 100:
            step = 5.0
        elif y_range <= 1000:
            step = 50.0
        else:
            step = 500.0
        
        return step


    def add_plot(self, time_points_list, values_list, file_name, y_label):

        is_new_or_reset = False

        # Find the target window by name, or create a new one
        target = self.target_window_var.get()
        win_dict = next(
            (w for w in self.windows if w.get('name') == target and w['window'].winfo_exists()),
            None
        )
        if win_dict is None:
            win_dict = self._create_window()
            self._window_counter += 1
            win_dict['name'] = (file_name[:40] if file_name else f"Window {self._window_counter}")
            win_dict['window'].title(f"Plot — {win_dict['name']}")
            self.target_window_var.set(win_dict['name'])
            self._notify_windows_changed()
            is_new_or_reset = True
        
        ax = win_dict['ax']
        canvas = win_dict['canvas']
        lines = win_dict['lines']
        labels = win_dict['labels']
        line_vars = win_dict['line_vars']
        control_frame = win_dict['control_frame']
        curve_list_canvas = win_dict['curve_list_canvas'] 
        
        # Rebuild checkbuttons to include the new curve
        for widget in control_frame.winfo_children():
            widget.destroy()


        # **MODIFICATION 2: Calculate dynamic X-axis limit**
        all_time_points = []
        
        # 1. Existing lines in the figure (if adding to existing)
        for line in lines:
            all_time_points.extend(line.get_xdata())
            
        # 2. New curves being added
        for tp in time_points_list:
            all_time_points.extend(tp)

        # Calculate max X-limit: minimum of 1.0, but extend if data goes beyond.
        max_time = 1.0 
        if all_time_points:
            max_time = max(max_time, np.max(all_time_points))
            
        # Set X-axis limits
        ax.set_xlim(0.0, max_time)

        # If any curve exceeds 1.0, show raw values on X axis; otherwise show %
        if max_time > 1.0:
            ax.xaxis.set_major_formatter(FuncFormatter(lambda x, pos: f'{x:.2f}'))
        else:
            ax.xaxis.set_major_formatter(FuncFormatter(lambda x, pos: f'{int(x*100)}%'))

        # Reapply the major locator for the new range
        try:
             interval = float(self.x_tick_entry_var.get())
        except ValueError:
             interval = 0.1

        # Safety cap: never generate more than ~20 ticks on X axis
        if max_time > 0 and max_time / interval > 20:
            interval = self._calculate_initial_y_step(max_time)
            self.x_tick_entry_var.set(f"{interval}")

        ax.xaxis.set_major_locator(matplotlib.ticker.MultipleLocator(interval))


        # Listor för alla linjer (inklusive de som fanns innan) som ska återskapas i Checkbuttons
        lines_to_add_to_ui = []
        labels_to_add_to_ui = []
        vars_to_add_to_ui = []
        all_values = [] # Samla alla Y-värden

        # Steg 1: Samla befintliga linjer
        for line in lines:
            lines_to_add_to_ui.append(line)
            labels_to_add_to_ui.append(labels[lines.index(line)])
            vars_to_add_to_ui.append(line_vars[lines.index(line)])
            # Exkludera NaN-värden från min/max-beräkning
            all_values.extend([v for v in line.get_ydata() if not np.isnan(v)])
        
        # Steg 2: Lägg till de nya linjerna
        for i, (tp, vals) in enumerate(zip(time_points_list, values_list)):
            # Endast lägg till Senior/Elder om det finns två kurvor
            if len(values_list) == 1:
                label = file_name
            else:
                label = f"{file_name} (Senior)" if i == 0 else f"{file_name} (Elder)"
            
            line, = ax.plot(tp, vals, marker=None, linestyle='-', label=label, picker=5, visible=True) 
            
            lines.append(line)
            labels.append(label)
            # Kryssrutan är FALSE som standard (inte vald för borttagning)
            var = tk.BooleanVar(value=False) 
            line_vars.append(var)
            
            lines_to_add_to_ui.append(line)
            labels_to_add_to_ui.append(label)
            vars_to_add_to_ui.append(var)

            # Lägg till nya Y-värden
            all_values.extend([v for v in vals if not np.isnan(v)])
        
        # Steg 3: Återskapa Checkbuttons i inner_control_frame
        for label, var in zip(labels_to_add_to_ui, vars_to_add_to_ui):
            # Checkbutton har ingen 'command' och styr INTE synlighet.
            cb = tk.Checkbutton(control_frame, text=label, variable=var)
            cb.pack(side='left', padx=2)
            
        # Uppdatera scrollregionen
        control_frame.update_idletasks() # Måste tvinga fram en layout-passering
        curve_list_canvas.configure(scrollregion=curve_list_canvas.bbox("all"))


        ax.set_ylabel(y_label)

        # Y-Axis Limit och Tick Logic
        # ----------------------------------------------------------------------
        
        # Hantera Y-limits
        if not all_values:
             ax.set_ylim(bottom=0.0, top=1.0) # Fallback
             initial_step = 0.1
        else:
             min_y = np.min(all_values)
             max_y = np.max(all_values)
             
             # Säkerhetsmarginal
             y_range_data = max_y - min_y
             y_min = max(0.0, min_y - y_range_data * 0.05) if y_range_data > 0 else max(0.0, min_y * 0.95)
             y_max = max_y + y_range_data * 0.05 if y_range_data > 0 else max_y * 1.05
             
             ax.set_ylim(bottom=y_min, top=y_max)
             
             # Beräkna initialt steg baserat på den nya skalan (med 5% marginal)
             y_min_with_margin, y_max_with_margin = ax.get_ylim()
             range_with_margin = y_max_with_margin - y_min_with_margin
             
             initial_step = self._calculate_initial_y_step(range_with_margin)
             
        # Always recalculate Y-tick step from current combined data range
        self.y_tick_entry_var.set(f"{initial_step}")

        # Applicera locator baserat på det aktuella värdet i entry-fältet
        self._apply_y_ticks(win_dict) 
        # ----------------------------------------------------------------------

        # Legend inside top-left corner
        # Ta bort och återskapa legenden
        if ax.get_legend() is not None:
             ax.get_legend().remove()
             
        # Build legend from data curves + visible growth lines
        visible_lines_final = [l for l in lines if l.get_label() not in ['_nolegend_', '']]
        visible_lines_final += [l for l in win_dict['vertical_lines'] if l.get_label() not in ['_nolegend_', '']]
        visible_labels_final = [l.get_label() for l in visible_lines_final]
        if visible_lines_final:
            ax.legend(visible_lines_final, visible_labels_final, loc='upper left', fontsize='small')
            
        canvas.draw_idle()

        
        
    def _on_hover(self, event, lines, annot):
        if not lines:
            return
        if event.xdata is None or event.ydata is None:
            return

        vis = annot.get_visible()

        for line in lines:
            # Use Matplotlib's built-in pixel-distance check (picker=5 on each line)
            cont, ind = line.contains(event)
            if not cont:
                continue

            xdata = line.get_xdata()
            ydata = line.get_ydata()

            # Pick the closest non-NaN point among the candidates
            best_idx = None
            best_dist = float('inf')
            for i in ind['ind']:
                if i < len(ydata) and not np.isnan(ydata[i]):
                    dist = abs(event.xdata - xdata[i])
                    if dist < best_dist:
                        best_dist = dist
                        best_idx = i

            if best_idx is None:
                continue

            x = xdata[best_idx]
            y = ydata[best_idx]

            if x <= 1.0:
                time_label = f"Time: {x*100:.1f}%"
            else:
                time_label = f"Time: {x:.2f}"

            annot.xy = (x, y)
            annot.set_text(f"{time_label}\nValue: {y:.2f}")
            annot.set_visible(True)
            line.figure.canvas.draw_idle()
            return

        if vis:
            annot.set_visible(False)
            if lines:
                lines[0].figure.canvas.draw_idle()

    # _remove_selected tar nu emot win_dict.
    def _remove_selected(self, win_dict: Dict):
        """Removes the curves and their associated checkbuttons that are selected for removal."""
        lines = win_dict['lines']
        labels = win_dict['labels']
        line_vars = win_dict['line_vars']
        ax = win_dict['ax']
        canvas = win_dict['canvas']
        control_frame = win_dict['control_frame']
        curve_list_canvas = win_dict['curve_list_canvas']

        widgets_to_remove = []
        # Samla Y-data för att omkalibrera Y-axeln efter borttagning
        remaining_values = [] 

        for i in reversed(range(len(lines))):
            if line_vars[i].get(): # Only remove if the Checkbutton is checked (True)
                lines[i].remove()
                
                # Find the corresponding Checkbutton in the scrollable frame
                for widget in control_frame.winfo_children():
                    if isinstance(widget, tk.Checkbutton) and widget.cget('text') == labels[i]:
                        widgets_to_remove.append(widget)
                        break

                del lines[i]
                del labels[i]
                del line_vars[i]
            else:
                 # Lägg till Y-data från kvarvarande linjer
                 remaining_values.extend([v for v in lines[i].get_ydata() if not np.isnan(v)])

        # Destroy identified Checkbutton widgets
        for widget in widgets_to_remove:
            widget.destroy()
            
        # Update scroll region
        control_frame.update_idletasks()
        curve_list_canvas.configure(scrollregion=curve_list_canvas.bbox("all"))
            
        # Återställ Y-axeln efter borttagning
        if not remaining_values:
            ax.set_ylim(bottom=0.0, top=1.0)
            self.y_tick_entry_var.set("0.1")
        else:
            min_y = np.min(remaining_values)
            max_y = np.max(remaining_values)
            y_range_data = max_y - min_y
            y_min = max(0.0, min_y - y_range_data * 0.05) if y_range_data > 0 else max(0.0, min_y * 0.95)
            y_max = max_y + y_range_data * 0.05 if y_range_data > 0 else max_y * 1.05
            ax.set_ylim(bottom=y_min, top=y_max)
            
            # Uppdatera entry-fältet med nytt beräknat steg baserat på det nya intervallet.
            y_min_with_margin, y_max_with_margin = ax.get_ylim()
            range_with_margin = y_max_with_margin - y_min_with_margin
            initial_step = self._calculate_initial_y_step(range_with_margin)
            self.y_tick_entry_var.set(f"{initial_step}")

        # Applicera det nya steget på axeln.
        self._apply_y_ticks(win_dict) 

        # Update the Legend
        if ax.get_legend() is not None:
             ax.get_legend().remove()
             
        # Skapa den nya legenden endast med de återstående linjerna
        visible_lines_final = [l for l in lines if l.get_label() not in ['_nolegend_', '']]
        visible_labels_final = [l.get_label() for l in visible_lines_final]
        if visible_lines_final:
            ax.legend(visible_lines_final, visible_labels_final, loc='upper left', fontsize='small')

        canvas.draw_idle()

# ------------------------- Table popups -------------------------
def show_balance_table(parent: tk.Tk, file_path: str):
    """Displays the data from a BalanceAttributes file in a new window."""
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            data = json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        messagebox.showerror("Error", f"Could not read or parse file {file_path}")
        return

    table_window = Toplevel(parent)
    table_window.title(os.path.basename(file_path))
    text_widget = Text(table_window, wrap='word')
    text_widget.pack(expand=True, fill='both', side='left')
    scrollbar = Scrollbar(table_window, command=text_widget.yview)
    scrollbar.pack(side='right', fill='y')
    text_widget.config(yscrollcommand=scrollbar.set)

    table_string = ""
    try:
        rows = data[0].get("Rows", {})
        for key, value_dict in rows.items():
            value = value_dict.get("AttributePercentageValues") or value_dict.get("AttributePercentageValue") or value_dict.get("Value")
            if value is not None:
                table_string += f"{key}: {value}\n"
    except Exception:
        table_string = "Error parsing the BalanceAttributes file."

    text_widget.insert(END, table_string)
    text_widget.config(state='disabled')

def show_calculated_stats_table(parent: tk.Tk, calculated_stats: Dict[str, float]):
    """Displays calculated survival stats in a new window."""
    stats_window = Toplevel(parent)
    stats_window.title("Calculated Survival Times")
    text_widget = Text(stats_window, wrap='word')
    text_widget.pack(expand=True, fill='both', side='left')
    table_string = "Survival Times (100% to 0%):\n\n"
    for key, seconds in calculated_stats.items():
        hours = int(seconds // 3600)
        minutes = int((seconds % 3600) // 60)
        remaining_seconds = round(seconds % 60, 2)
        time_parts = []
        if hours > 0: time_parts.append(f"{hours}h")
        if minutes > 0: time_parts.append(f"{minutes}m")
        if remaining_seconds > 0 or not time_parts: time_parts.append(f"{remaining_seconds}s")
        formatted_time = " ".join(time_parts)
        table_string += f"{key}:\n  Total Seconds: {seconds:.2f}\n  Formatted Time: {formatted_time}\n\n"
    text_widget.insert(END, table_string)
    text_widget.config(state='disabled')

# ------------------------- Main UI -------------------------
class JSONPlotterUI:
    CALCULATED_STATS_KEY = "Calculated Survival Times"

    def __init__(self, master: tk.Tk):
        self.master = master
        master.title("JSON Plotter")

        self.data_loader: DataLoader = None  # set in auto locate
        self.folders: List[str] = []
        self.json_files_paths: Dict[str, str] = {}
        self.virtual_files_data: Dict[str, Dict[str, Any]] = {}
        self.calculated_stats_data: Dict[str, float] = {}

        self.overlay_plotter = OverlayPlotter(master)
        self.overlay_plotter.on_windows_changed = self._update_window_selector

        # Variables
        self.folder_var = tk.StringVar(master)
        self.json_file_var = tk.StringVar(master)
        self.override_path_var = tk.BooleanVar(master)

        # Widgets
        self._create_widgets()

        # Autolocate JSONs near exe/script
        self.auto_locate_jsons_folder()

    def _create_widgets(self):
        top_frame = tk.Frame(self.master)
        top_frame.pack(fill='x', padx=6, pady=6)

        self.override_check = tk.Checkbutton(top_frame, text="Override JSON Folder Location", variable=self.override_path_var, command=self._on_override_toggle)
        self.override_check.pack(side='left')

        self.path_entry = tk.Entry(top_frame, width=60)
        self.path_entry.pack(side='left', padx=6)
        self.path_entry.config(state='disabled')

        self.browse_btn = tk.Button(top_frame, text="Browse…", command=self._browse_override, state='disabled')
        self.browse_btn.pack(side='left')

        mid = tk.Frame(self.master)
        mid.pack(fill='x', padx=6, pady=6)

        tk.Label(mid, text="Select Dinosaur:").pack(side='left')
        self.folder_menu = tk.OptionMenu(mid, self.folder_var, ())
        self.folder_menu.pack(side='left', padx=6)

        tk.Label(mid, text="Select Attribute:").pack(side='left')
        self.json_menu = tk.OptionMenu(mid, self.json_file_var, ())
        self.json_menu.pack(side='left', padx=6)

        btn_frame = tk.Frame(self.master)
        btn_frame.pack(fill='x', padx=6, pady=6)
        self.plot_button = tk.Button(btn_frame, text="Plot Data", command=self.plot_selected_file, state='disabled')
        self.plot_button.pack(side='left', padx=6)

        self.refresh_btn = tk.Button(btn_frame, text='Refresh', command=self.auto_locate_jsons_folder)
        self.refresh_btn.pack(side='left')

        tk.Label(btn_frame, text="Target Window:").pack(side='left', padx=(20, 2))
        self._window_selector = ttk.Combobox(
            btn_frame,
            textvariable=self.overlay_plotter.target_window_var,
            values=["New Window"],
            width=28,
            state='readonly'
        )
        self._window_selector.pack(side='left')

    def _update_window_selector(self):
        """Refresh the window-target combobox whenever plot windows open or close."""
        names = ["New Window"] + [
            w['name'] for w in self.overlay_plotter.windows
            if w['window'].winfo_exists() and w.get('name')
        ]
        self._window_selector['values'] = names
        if self.overlay_plotter.target_window_var.get() not in names:
            self.overlay_plotter.target_window_var.set("New Window")

    def _on_override_toggle(self):
        enabled = self.override_path_var.get()
        self.path_entry.config(state='normal' if enabled else 'disabled')
        self.browse_btn.config(state='normal' if enabled else 'disabled')
        if not enabled:
            self.auto_locate_jsons_folder()

    def _browse_override(self):
        selected = filedialog.askdirectory(title='Select JSONs folder (root containing species folders)')
        if selected:
            self.path_entry.delete(0, END)
            self.path_entry.insert(0, selected)
            # Use the override immediately
            self.data_loader = DataLoader(selected)
            self.folders = self.data_loader.find_dinosaurs()
            self._update_folder_menu()

    def auto_locate_jsons_folder(self):
        """Finds the JSONs folder next to the EXE/script."""
        if self.override_path_var.get():
            path = self.path_entry.get().strip()
            if path and os.path.isdir(path):
                root_dir = path
            else:
                messagebox.showwarning("Invalid path", "Override path invalid or missing.")
                return
        else:
            root_dir = find_jsons_folder_next_to_app()
            if not root_dir:
                fallback = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'JSONs')
                root_dir = fallback if os.path.isdir(fallback) else ''

        if not root_dir:
            messagebox.showwarning("JSONs not found", "Could not find a 'JSONs' folder next to the application. Use override or place the folder next to the EXE/script.")
            self.data_loader = DataLoader('')  # empty loader
            self.folders = []
            self._update_folder_menu()
            return

        self.data_loader = DataLoader(root_dir)
        self.folders = self.data_loader.find_dinosaurs()
        self._update_folder_menu()
        
    def _on_folder_selected(self, folder_name: str):
        """Called when a dinosaur folder is selected, updates the file list."""
        self.folder_var.set(folder_name)
        if self.data_loader:
            self.json_files_paths = self.data_loader.find_plottable_files(folder_name)
            self.virtual_files_data = self.data_loader.generate_virtual_attack_files(folder_name)
            self.calculated_stats_data = self.data_loader.generate_calculated_stats(folder_name)
            self._update_json_menu()

    def _update_folder_menu(self):
        menu = self.folder_menu['menu']
        menu.delete(0, 'end')
        if not self.folders:
            menu.add_command(label='No Dinosaurs found', command=lambda: None)
            self.folder_var.set('')
            self.json_files_paths = {}
            self.virtual_files_data = {}
            self.calculated_stats_data = {}
            self._update_json_menu()
            return

        for folder in self.folders:
            # Note: This calls the restored _on_folder_selected method
            menu.add_command(label=folder, command=lambda value=folder: self._on_folder_selected(value))
        if not self.folder_var.get() or self.folder_var.get() not in self.folders:
            self.folder_var.set(self.folders[0])
        # Note: This calls the restored _on_folder_selected method
        self._on_folder_selected(self.folder_var.get())

    def _on_json_selected(self, json_name: str):
        """Sets the selected JSON file name and ensures the plot button is enabled."""
        self.json_file_var.set(json_name)
        
        # Uppdatera knappens text beroende på om det är statistik/attribut eller BalanceAttributes.
        if json_name == self.CALCULATED_STATS_KEY:
            self.plot_button.config(text="Show Calculated Stats")
        elif json_name in self.json_files_paths and 'BalanceAttributes' in self.json_files_paths[json_name]:
             self.plot_button.config(text="Show Balance Attributes")
        else:
             self.plot_button.config(text="Plot Data")
             
        self.plot_button.config(state='normal')

    def _update_json_menu(self):
        menu = self.json_menu['menu']
        menu.delete(0, 'end')
        display_names = sorted(list(self.json_files_paths.keys()) + list(self.virtual_files_data.keys()))
        if self.calculated_stats_data:
            display_names.append(self.CALCULATED_STATS_KEY)
        if not display_names:
            menu.add_command(label='No files found', command=lambda: None)
            self.json_file_var.set('')
            self.plot_button.config(state='disabled')
            return
        for name in display_names:
            menu.add_command(label=name, command=lambda v=name: self._on_json_selected(v))
            
        if not self.json_file_var.get() or self.json_file_var.get() not in display_names:
            self.json_file_var.set(display_names[0])
            
        self.plot_button.config(state='normal')
        
        self._on_json_selected(self.json_file_var.get()) 

    def plot_selected_file(self):
        name = self.json_file_var.get()
        if not name:
            return

        # ============================
        # CALCULATED STATS
        # ============================
        if name == self.CALCULATED_STATS_KEY:
            show_calculated_stats_table(self.master, self.calculated_stats_data)
            return

        # ============================
        # VIRTUAL ATTACK FILES
        # ============================
        if name in self.virtual_files_data:
            data = self.virtual_files_data[name]
            time_points_list = [c['Time'] for c in data['curves']]
            values_list = [c['Values'] for c in data['curves']]
            self.overlay_plotter.add_plot(time_points_list, values_list, data['title_name'], data['y_label'])
            return

        # ============================
        # REGULAR ATTRIBUTE FILE
        # ============================
        file_path = self.json_files_paths.get(name)
        if not file_path or not os.path.isfile(file_path):
            messagebox.showerror('File not found', f'File not found: {file_path}')
            return

        # ============================
        # BALANCE ATTRIBUTES TABLE
        # ============================
        if 'BalanceAttributes' in os.path.basename(file_path):
            show_balance_table(self.master, file_path)
            return

        # ====================================================================
        #   >>>>>>>>> WEIGHT CRASH PRELOAD: BEPI AUTO-LOAD + REMOVE <<<<<<<<<
        # ====================================================================
        lower_name = name.lower()
        is_weight = "weight" in lower_name

        if is_weight:
            bepi_path = os.path.join(
                self.data_loader.root_dir,
                "Beipiaosaurus", "Attributes",
                "ATT_Beipiaosaurus_Weight.json"
            )

            if os.path.isfile(bepi_path):
                # 1. Load BEPI silently
                tp_b, vp_b, yl_b, title_b = self.data_loader.get_plot_data(bepi_path, "TEMP_BEPI")

                # 2. Plot BEPI into window
                self.overlay_plotter.add_plot(tp_b, vp_b, "TEMP_BEPI", yl_b)

                # 3. Mark BEPI for removal
                win = self.overlay_plotter.windows[-1]
                for i, lbl in enumerate(win['labels']):
                    if lbl == "TEMP_BEPI":
                        win['line_vars'][i].set(True)

                # 4. Remove BEPI from axis + UI
                self.overlay_plotter._remove_selected(win)

                # 5. HARD AXIS RESET (critical for correct scaling)
                ax = win["ax"]
                ax.clear()

                # >>> RESET INTERNAL LINE STATE (fixes hover crash) <<<
                win["lines"].clear()
                win["labels"].clear()
                win["line_vars"].clear()
                win["vertical_lines"].clear()

                # 6. Restore axis formatter and grid
                def safe_percent_formatter(x, pos):
                    if x < 0:
                        return ""
                    if x <= 1.0000001:
                        return f"{round(x * 100)}%"
                    return f"{x:.2f}"

                ax.set_xlabel("Time")
                ax.grid(True)
                ax.xaxis.set_major_formatter(FuncFormatter(safe_percent_formatter))
                ax.xaxis.set_major_locator(matplotlib.ticker.MultipleLocator(0.1))

                win["canvas"].draw_idle()

        # ====================================================================
        #                        FINAL: PLOT REAL CURVE
        # ====================================================================

        time_points_list, values_list, y_label, title = self.data_loader.get_plot_data(file_path, name)

        if not time_points_list or not values_list:
            messagebox.showinfo('No plot data', 'No plotable data found in that file.')
            return

        self.overlay_plotter.add_plot(time_points_list, values_list, title, y_label)

# ------------------------- Run -------------------------
def main():
    root = tk.Tk()
    root.geometry('1100x140')
    app = JSONPlotterUI(root)
    root.mainloop()

if __name__ == '__main__':
    main()