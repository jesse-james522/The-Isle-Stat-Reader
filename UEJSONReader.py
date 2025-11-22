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
            for curve in (ap_curves[:2] if isinstance(ap_curves, list) else [ap_curves]):
                keys = curve.get("Keys") if isinstance(curve, dict) else None
                if not keys or not isinstance(keys, list) or len(keys) == 0:
                    continue
                
                # Ny, korrekt interpolationslogik (återanvänds)
                processed_time_points = []
                processed_values = []
                
                # Lägg till första punkten manuellt
                key_first = keys[0]
                if not isinstance(key_first, dict): continue # Skydd
                
                t_first = float(key_first.get("Time", 0.0))
                v_first = float(key_first.get("Value", 0.0))
                
                if t_first >= 0.0:
                    processed_time_points.append(t_first)
                    processed_values.append(v_first * float(damage_value))
                else:
                    continue # Hoppa över hela kurvan om den börjar före 0.0

                # Loopa över alla segment
                for i in range(len(keys) - 1):
                    key1 = keys[i]
                    key2 = keys[i+1]

                    if not isinstance(key1, dict) or not isinstance(key2, dict): continue

                    t1 = float(key1.get("Time", 0.0))
                    v1_raw = float(key1.get("Value", 0.0))
                    m1_raw = float(key1.get("LeaveTangent", 0.0))
                    
                    t2 = float(key2.get("Time", 0.0))
                    v2_raw = float(key2.get("Value", 0.0))
                    m2_raw = float(key2.get("ArriveTangent", 0.0))
                    
                    # InterpMode på key2 definierar segmentet FRÅN key1 TILL key2
                    interp_mode = key2.get("InterpMode", "RCIM_Linear")

                    if interp_mode == "RCIM_Cubic":
                        t_cubic, v_cubic_raw = calculate_cubic_segment(
                            t1, v1_raw, m1_raw, 
                            t2, v2_raw, m2_raw
                        )
                        # Lägg till de nya interpolerade punkterna (hoppa över den första,
                        # eftersom den redan lades till i föregående iteration)
                        processed_time_points.extend(t_cubic[1:])
                        processed_values.extend(list(v_cubic_raw[1:] * float(damage_value)))
                    else: # RCIM_Linear
                        # FIX: Sample linear segment for hover accuracy
                        if t2 > t1 and not np.isclose(t2, t1):
                            num_points = 25
                            t_linear = np.linspace(t1, t2, num_points)
                            
                            v1 = v1_raw * float(damage_value)
                            v2 = v2_raw * float(damage_value)
                            
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
                                processed_values.append(v2_raw * float(damage_value))
                
                if processed_time_points and processed_values:
                    new_curves.append({"Time": processed_time_points, "Values": processed_values})

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

        try:
            rows = balance_data[0].get("Rows", {}) if isinstance(balance_data, list) and balance_data else (balance_data.get("Rows", {}) if isinstance(balance_data, dict) else {})
            decay_stats = {
                k: v.get("AttributePercentageValues") or v.get("AttributePercentageValue") or v.get("Value")
                for k, v in rows.items()
                if ("Decay" in k) and (k.startswith("Hunger.") or k.startswith("Thirst."))
            }

            for key, value in decay_stats.items():
                if value is not None and value != 0:
                    calculated_value = 100 / abs(value)
                    if key == "Hunger.Decay":
                        display_name = "Time to Starve (100%->0%)"
                    elif key == "Thirst.Decay":
                        display_name = "Time to Dehydrate (100%->0%)"
                    else:
                        display_name = key.replace(".Decay", " Time")
                    calculated_stats[display_name] = calculated_value
        except Exception:
            return {}
        return calculated_stats

    def _process_tyrannosaurus_weight_curves(self, curve_data_list: List[Dict[str, Any]], conversion_factor: float, file_name: str) -> Tuple[List[List[float]], List[List[float]]]:
        """
        Special case handling for Tyrannosaurus Weight where the Senior curve's
        interpolation must be forced to match the Elder curve before the split
        at 0.75 due to UE5 JSON export tangent differences.
        """
        if len(curve_data_list) < 2:
            return self._process_generic_curves(curve_data_list, conversion_factor)

        # Assuming curve_data_list[0] is Senior (plateaus earlier) and [1] is Elder (plateaus later)
        # We process the Elder (master) curve fully first.
        elder_keys = curve_data_list[1].get("Keys")
        senior_keys = curve_data_list[0].get("Keys")

        # 1. Process Elder Curve (Master)
        time_points_elder, values_elder = self._interpolate_keys(elder_keys, conversion_factor)
        
        # FIX 1: Enforce plateau for Elder curve (extends to 1.0)
        if time_points_elder and time_points_elder[-1] < 1.0:
            last_value = values_elder[-1]
            time_points_elder.append(1.0)
            values_elder.append(last_value)
        
        # 2. Process Senior Curve (Slave/Branch)
        
        # Find the last key of the Senior curve (where it should plateau)
        senior_last_key_time = senior_keys[-1].get("Time", 0.0) if senior_keys else 0.0
        senior_last_key_value = senior_keys[-1].get("Value", 0.0) * conversion_factor if senior_keys else 0.0

        # Find the index in the Elder's interpolated list corresponding to the Senior's last key time
        time_points_np = np.array(time_points_elder)
        # Find all indices where the time is less than or equal to the Senior's last key time
        # This includes the final point of the Elder curve segment that the Senior curve follows.
        split_index = np.searchsorted(time_points_np, senior_last_key_time, side='right')

        # Senior curve data takes the Elder's interpolated data up to the split point
        # Using simple list slicing for list objects (no .tolist() needed)
        time_points_senior = time_points_elder[:split_index]
        values_senior = values_elder[:split_index]

        # Enforce plateau at the end of the curve (extends to 1.0)
        if time_points_senior and time_points_senior[-1] < 1.0:
            # Check if the last point added is actually the senior_last_key_time, if not add it
            if not np.isclose(time_points_senior[-1], senior_last_key_time):
                 time_points_senior.append(senior_last_key_time)
                 # We use the final raw value for stability
                 values_senior.append(senior_last_key_value)
            
            # Extend to 1.0 with the plateau value
            time_points_senior.append(1.0)
            values_senior.append(senior_last_key_value)

        return [time_points_senior, time_points_elder], [values_senior, values_elder]


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
            
            interp_mode = key2.get("InterpMode", "RCIM_Linear")

            if interp_mode == "RCIM_Cubic":
                t_cubic, v_cubic_raw = calculate_cubic_segment(
                    t1, v1_raw, m1_raw, 
                    t2, v2_raw, m2_raw
                )
                processed_time_points.extend(t_cubic[1:])
                processed_values.extend(list(v_cubic_raw[1:] * conversion_factor))
            else: # RCIM_Linear
                # FIX 2: Sample linear segment for hover accuracy
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
        """Processes generic curves without special Tyrannosaurus Weight logic."""
        time_points_list: List[List[float]] = []
        values_list: List[List[float]] = []

        for curve in curve_data_list:
            keys = curve.get("Keys")
            if not keys or not isinstance(keys, list) or len(keys) == 0:
                continue
            
            time_points, values = self._interpolate_keys(keys, conversion_factor)

            # Enforce plateau at the end of the curve (extends to 1.0)
            if time_points and time_points[-1] < 1.0:
                last_value = values[-1]
                time_points.append(1.0)
                values.append(last_value)

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
            float_curves = item.get("FloatCurves") or (item.get("Properties", {}).get("FloatCurves") if isinstance(item, dict) else None)
            
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

        # --- SPECIAL HANDLING FOR TYRANNOSAURUS WEIGHT ---
        if "Tyrannosaurus Weight" in file_name and len(float_curves) >= 2:
            time_points_list, values_list = self._process_tyrannosaurus_weight_curves(
                float_curves[:2], conversion_factor, file_name
            )
        else:
            # --- GENERIC HANDLING ---
            time_points_list, values_list = self._process_generic_curves(
                float_curves[:2], conversion_factor
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
        self.add_to_existing = tk.BooleanVar(master, value=True)  # toggle

    def _create_window(self) -> Dict:
        win = Toplevel(self.master)
        win.title("Overlay Plot")
        
        fig = Figure(figsize=(10, 6), dpi=100)
        ax = fig.add_subplot(111)
        ax.set_xlabel("Time")
        ax.grid(True)
        ax.axvline(x=0.75, color='r', linestyle='--', label='elder split')
        ax.xaxis.set_major_formatter(FuncFormatter(lambda x, pos: f'{int(x*100)}%'))
        ax.xaxis.set_major_locator(matplotlib.ticker.MultipleLocator(0.1))

        canvas = FigureCanvasTkAgg(fig, master=win)
        canvas.get_tk_widget().pack(fill='both', expand=True)

        annot = ax.annotate("", xy=(0,0), xytext=(20,20), textcoords='offset points',
                            bbox=dict(boxstyle='round', fc='yellow', alpha=0.8),
                            arrowprops=dict(arrowstyle='->'))
        annot.set_visible(False)

        lines: List[Any] = []
        labels: List[str] = []
        line_vars: List[tk.BooleanVar] = []
        
        canvas.mpl_connect('motion_notify_event', lambda event: self._on_hover(event, lines, annot))

        # Control frame for removing individual graphs
        control_frame = tk.Frame(win)
        control_frame.pack(fill='x', padx=5, pady=5)
        tk.Label(control_frame, text="Remove Individual Graphs:").pack(side='left')
        remove_btn = tk.Button(control_frame, text="Remove Selected",
                               command=lambda: self._remove_selected(lines, labels, line_vars, ax, canvas, control_frame))
        remove_btn.pack(side='left')
        tk.Checkbutton(control_frame, text="Add to Existing", variable=self.add_to_existing).pack(side='left')

        win_dict = {
            'window': win,
            'fig': fig,
            'ax': ax,
            'canvas': canvas,
            'lines': lines,
            'labels': labels,
            'line_vars': line_vars,
            'annot': annot,
            'control_frame': control_frame
        }

        # Remove window from list when closed
        def on_close():
            if win_dict in self.windows:
                self.windows.remove(win_dict)
            win.destroy()

        win.protocol("WM_DELETE_WINDOW", on_close)

        self.windows.append(win_dict)
        return win_dict

    def add_plot(self, time_points_list, values_list, file_name, y_label):
        # Check for an existing window to reuse
        if self.add_to_existing.get() and self.windows:
            win_dict = self.windows[-1]
            if not win_dict['window'].winfo_exists():
                win_dict = self._create_window()
        else:
            win_dict = self._create_window()
        
        ax = win_dict['ax']
        canvas = win_dict['canvas']
        lines = win_dict['lines']
        labels = win_dict['labels']
        line_vars = win_dict['line_vars']
        control_frame = win_dict['control_frame']

        # Skip Elder if identical
        if len(values_list) > 1 and len(values_list[0]) == len(values_list[1]) and \
           all(abs(a-b) < 1e-9 for a,b in zip(values_list[0], values_list[1])):
            time_points_list = time_points_list[:1]
            values_list = values_list[:1]

        for i, (tp, vals) in enumerate(zip(time_points_list, values_list)):
            # Only add Senior/Elder if there are two curves
            if len(values_list) == 1:
                label = file_name
            else:
                label = f"{file_name} (Senior)" if i == 0 else f"{file_name} (Elder)"
            
            line, = ax.plot(tp, vals, marker=None, linestyle='-', label=label, picker=5) # picker=5 gör linjen klickbar
            
            lines.append(line)
            labels.append(label)
            var = tk.BooleanVar(value=False)
            line_vars.append(var)
            cb = tk.Checkbutton(control_frame, text=label, variable=var)
            cb.pack(side='left', padx=2)

        ax.set_ylabel(y_label)
        ax.set_title("Overlayed JSON Graphs")

        ymin, ymax = ax.get_ylim()
        yrange = ymax - ymin

        if yrange <= 10:
            step = 1
        elif yrange <= 100:
            step = 5
        elif yrange <= 1000:
            step = 50
        else:
            step = 500

        ax.yaxis.set_major_locator(matplotlib.ticker.MultipleLocator(step))

        # Legend inside top-left corner
        ax.legend(loc='upper left', fontsize='small')

        canvas.draw_idle()

        
        
    def _on_hover(self, event, lines, annot):
        if not lines:
            return
        
        vis = annot.get_visible()
        
        for line in lines:
            if not line.get_visible():
                continue
            
            # Kolla om musen är "nästan" på linjen
            cont, ind = line.contains(event)
            if not cont:
                # 'contains' är inte perfekt för linjer, så vi gör en extra koll
                # om event.xdata och event.ydata finns.
                if event.xdata is None or event.ydata is None:
                    continue
            
            if event.xdata is None: continue
            
            xdata = line.get_xdata()
            ydata = line.get_ydata()
            
            # Hitta index för närmaste x-värde i linjens data
            idx = np.searchsorted(xdata, event.xdata)
            
            if idx == 0:
                idx_closest = 0
            elif idx == len(xdata):
                idx_closest = len(xdata) - 1
            else:
                # Jämför avstånd till punkten före och efter
                if abs(event.xdata - xdata[idx-1]) < abs(event.xdata - xdata[idx]):
                    idx_closest = idx-1
                else:
                    idx_closest = idx
            
            x = xdata[idx_closest]
            y = ydata[idx_closest]
            
            # Tröskel: Muspekaren måste vara tillräckligt nära x-punkten
            x_range = line.axes.get_xlim()
            if abs(event.xdata - x) > (x_range[1] - x_range[0]) * 0.02: # 2% tröskel
                continue 
            
            # Tröskel: Muspekaren måste vara tillräckligt nära y-punkten
            y_range = line.axes.get_ylim()
            if abs(event.ydata - y) > (y_range[1] - y_range[0]) * 0.05: # 5% tröskel
                continue

            annot.xy = (x, y)
            annot.set_text(f"Time: {x*100:.1f}%\nValue: {y:.2f}")
            annot.set_visible(True)
            line.figure.canvas.draw_idle()
            return

        if vis:
            annot.set_visible(False)
            if lines:
                lines[0].figure.canvas.draw_idle()

    def _remove_selected(self, lines, labels, line_vars, ax, canvas, control_frame):
        for i in reversed(range(len(lines))):
            if line_vars[i].get():
                lines[i].remove()
                # Remove the checkbox
                for widget in control_frame.winfo_children():
                    if isinstance(widget, tk.Checkbutton) and widget.cget('text') == labels[i]:
                        widget.destroy()
                del lines[i]
                del labels[i]
                del line_vars[i]
        ax.legend(loc='upper left', fontsize='small')
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
            menu.add_command(label=folder, command=lambda value=folder: self._on_folder_selected(value))
        if not self.folder_var.get() or self.folder_var.get() not in self.folders:
            self.folder_var.set(self.folders[0])
        self._on_folder_selected(self.folder_var.get())

    def _on_folder_selected(self, folder_name: str):
        self.folder_var.set(folder_name)
        self.json_files_paths = self.data_loader.find_plottable_files(folder_name)
        self.virtual_files_data = self.data_loader.generate_virtual_attack_files(folder_name)
        self.calculated_stats_data = self.data_loader.generate_calculated_stats(folder_name)
        self._update_json_menu()

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
        if name == self.CALCULATED_STATS_KEY:
            show_calculated_stats_table(self.master, self.calculated_stats_data)
            return
        if name in self.virtual_files_data:
            data = self.virtual_files_data[name]
            time_points_list = [c['Time'] for c in data['curves']]
            values_list = [c['Values'] for c in data['curves']]
            self.overlay_plotter.add_plot(time_points_list, values_list, data['title_name'], data['y_label'])
            return
        file_path = self.json_files_paths.get(name)
        if not file_path or not os.path.isfile(file_path):
            messagebox.showerror('File not found', f'File not found: {file_path}')
            return
        if 'BalanceAttributes' in os.path.basename(file_path):
            show_balance_table(self.master, file_path)
            return
        time_points_list, values_list, y_label, title = self.data_loader.get_plot_data(file_path, name)
        if not time_points_list or not values_list:
            messagebox.showinfo('No plot data', 'No plotable data found in that file.')
            return
        self.overlay_plotter.add_plot(time_points_list, values_list, title, y_label)

# ------------------------- Run -------------------------
def main():
    root = tk.Tk()
    root.geometry('900x140')
    app = JSONPlotterUI(root)
    root.mainloop()

if __name__ == '__main__':
    main()