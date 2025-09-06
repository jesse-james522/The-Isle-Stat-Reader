import json
import matplotlib.pyplot as plt
from tkinter import Tk, Button, Label, OptionMenu, StringVar, Toplevel, Text, Scrollbar, END, Checkbutton, BooleanVar, Entry
import os
import glob
from matplotlib.ticker import FuncFormatter
import sys
import re
from pprint import pprint

# We need to set the backend to 'TkAgg' for proper Tkinter integration
# and interactive features like event handling.
try:
    from matplotlib.backends.backend_tkagg import FigureCanvasTkAgg
    plt.switch_backend('TkAgg')
except ImportError:
    print("Warning: TkAgg backend not found. Matplotlib interactivity may be limited.")

def get_json_data(file_path):
    """Safely loads and returns JSON data from a given file path."""
    try:
        with open(file_path, 'r') as f:
            return json.load(f)
    except FileNotFoundError:
        print(f"Error: The file {file_path} was not found.")
        return None
    except json.JSONDecodeError:
        print(f"Error: Invalid JSON format in {file_path}.")
        return None

def format_file_name(file_name, dinosaur_name):
    """Cleans up and formats the file name for display in the UI."""
    name = os.path.basename(file_name)
    name = name.replace(".json", "")
    if name.startswith("ATT_"):
        name = name.replace(f"ATT_{dinosaur_name}_", "")
    elif name.startswith("DT_"):
        name = name.replace(f"DT_{dinosaur_name}", "")
    name = re.sub(r'([A-Z])', r' \1', name).strip()
    return name

def format_virtual_name(name):
    """Cleans up and formats the virtual file name."""
    # Split words based on uppercase letters
    name = re.sub(r'([A-Z])', r' \1', name).strip()
    return name

def plot_data(time_points_list, values_list, file_name, y_label):
    """
    Plots the provided data on a graph with interactive tooltips and
    requested formatting.
    """
    if not values_list or not time_points_list:
        print("Error: No data to plot.")
        return

    # Create the figure and axes
    fig, ax = plt.subplots(figsize=(10, 6))

    all_lines = []
    y_max = 0

    # Plot each curve
    for i, (time_points, values) in enumerate(zip(time_points_list, values_list)):
        if i == 0:
            label = "Senior"
        else:
            label = "Elder"
        line, = ax.plot(time_points, values, marker='o', linestyle='-', label=label)
        all_lines.append(line)
        if values:
            y_max = max(y_max, max(values))

    # Prepare the tooltip annotation
    annot = ax.annotate("", xy=(0,0), xytext=(20, 20), textcoords="offset points",
                        bbox=dict(boxstyle="round,pad=0.3", fc="yellow", alpha=0.5),
                        arrowprops=dict(arrowstyle="->"))
    annot.set_visible(False)
    
    def update_annot(line_obj, index):
        """Updates the tooltip text and position, keeping it within bounds."""
        xdata, ydata = line_obj.get_data()
        x, y = xdata[index], ydata[index]
        
        # Determine tooltip vertical position
        y_offset = 20
        if y > y_max * 0.75:
            y_offset = -40
        
        # Determine tooltip horizontal position
        x_offset = 20
        if x > 0.75:
            x_offset = -120
        
        annot.xy = (x, y)
        annot.xytext = (x_offset, y_offset)
        
        text = f"Time: {x:.2f}\n{y_label}: {y:.2f}"
        annot.set_text(text)
        annot.set_visible(True)
        fig.canvas.draw_idle()

    def on_hover(event):
        """Event handler for mouse movement."""
        if event.inaxes != ax:
            if annot.get_visible():
                annot.set_visible(False)
                fig.canvas.draw_idle()
            return
        
        found_point = False
        for line in all_lines:
            contains, info = line.contains(event)
            if contains:
                index = info['ind'][0]
                update_annot(line, index)
                found_point = True
                break
        
        if not found_point:
            if annot.get_visible():
                annot.set_visible(False)
                fig.canvas.draw_idle()

    # Connect the hover event
    fig.canvas.mpl_connect("motion_notify_event", on_hover)

    # Add labels, title, and other plot customizations
    ax.set_xlabel('Time')
    ax.set_ylabel(y_label)
    ax.set_title(f'Plot of {file_name}', pad=20)
    ax.axvline(x=0.75, color='r', linestyle='--', label='elder split')
    ax.legend()
    ax.grid(True)
    
    # Format the x-axis ticks as percentages
    formatter = FuncFormatter(lambda x, pos: f'{int(x * 100)}%')
    ax.xaxis.set_major_formatter(formatter)
    
    # Show the plot in a new window
    plt.show(block=True)
    
def show_balance_table(file_path):
    """Displays the data from a BalanceAttributes file in a new window."""
    data = get_json_data(file_path)
    if not data:
        return

    table_window = Toplevel()
    table_window.title(os.path.basename(file_path))

    text_widget = Text(table_window, wrap='word')
    text_widget.pack(expand=True, fill='both', side='left')

    scrollbar = Scrollbar(table_window, command=text_widget.yview)
    scrollbar.pack(side='right', fill='y')
    text_widget.config(yscrollcommand=scrollbar.set)

    # Convert the JSON data to a formatted string
    table_string = ""
    try:
        rows = data[0].get("Rows", {})
        for key, value_dict in rows.items():
            value = value_dict.get("AttributePercentageValues")
            if value is not None:
                table_string += f"{key}: {value}\n"
    except (KeyError, IndexError):
        table_string = "Error parsing the BalanceAttributes file."
    
    text_widget.insert(END, table_string)
    text_widget.config(state='disabled') # Make it read-only

def is_linear_or_flat(file_path):
    """
    Checks if a JSON file represents a linear or flat curve based on its data points.
    A curve is considered linear or flat if it has two or fewer keyframes, or if
    the slope between all consecutive points is the same.
    """
    data = get_json_data(file_path)
    if not data:
        return True

    try:
        float_curves = data[0].get("FloatCurves") or [data[0].get("Properties", {}).get("FloatCurve")]
        keys = float_curves[0]["Keys"]
        
        # If there are 2 or fewer points, the curve is always a straight line
        if len(keys) <= 2:
            return True
        
        time_points = [key["Time"] for key in keys]
        values = [key["Value"] for key in keys]

        # Check for a flat curve (all values are the same)
        first_value = values[0]
        if all(v == first_value for v in values):
            return True

        # Check for a linear curve (all slopes are the same)
        # Avoid division by zero if time points are the same
        first_slope = (values[1] - values[0]) / (time_points[1] - time_points[0]) if time_points[1] - time_points[0] != 0 else float('inf')
        
        for i in range(2, len(keys)):
            # Avoid division by zero
            time_diff = time_points[i] - time_points[i-1]
            if time_diff == 0:
                current_slope = float('inf')
            else:
                current_slope = (values[i] - values[i-1]) / time_diff
            
            # Compare with tolerance for floating point numbers
            if abs(current_slope - first_slope) > 1e-9:
                return False

        return True # It passed all the checks, so it's linear.

    except (KeyError, IndexError, TypeError):
        return True # Assume it's unplottable or invalid if there's a parsing error

class JSONPlotterUI:
    def __init__(self, master):
        self.master = master
        master.title("JSON Plotter")
        
        self.root_dir = ""
        self.folders = []
        self.json_files_paths = {}
        self.virtual_files_data = {}

        self.folder_var = StringVar(master)
        self.json_file_var = StringVar(master)
        self.override_path_var = BooleanVar(master)

        # --- UI Elements ---
        self.override_path_check = Checkbutton(master, text="Override JSON Folder Location", variable=self.override_path_var, command=self.toggle_path_entry)
        self.override_path_check.pack(pady=5)

        self.path_entry = Entry(master, width=50, state="disabled")
        self.path_entry.pack(pady=5)
        self.path_entry.bind('<Return>', self.update_folder_from_entry)
        self.path_entry.bind('<FocusOut>', self.update_folder_from_entry)

        self.select_root_button = Button(master, text="Locating JSONs folder...", state="disabled", command=self.auto_locate_jsons_folder)
        self.select_root_button.pack(pady=5)

        self.folder_label = Label(master, text="Select Dinosaur:")
        self.folder_label.pack()
        self.folder_menu = OptionMenu(master, self.folder_var, "No folders found")
        self.folder_menu.pack()
        self.folder_var.trace("w", self.update_json_files)

        self.json_label = Label(master, text="Select Attribute (JSON File):")
        self.json_label.pack()
        self.json_menu = OptionMenu(master, self.json_file_var, "No files found")
        self.json_menu.pack()
        self.json_file_var.trace("w", self.enable_plot_button)
        
        self.plot_button = Button(master, text="Plot Data", command=self.plot_selected_file, state="disabled")
        self.plot_button.pack(pady=10)

        self.auto_locate_jsons_folder()

    def toggle_path_entry(self):
        """Toggles the state of the path entry field and updates the folder list."""
        if self.override_path_var.get():
            self.path_entry.config(state="normal")
            self.select_root_button.config(text="Use Manual Path", state="normal")
        else:
            self.path_entry.config(state="disabled")
            self.auto_locate_jsons_folder()
            self.select_root_button.config(text="Locating JSONs folder...", state="disabled")

    def update_folder_from_entry(self, event=None):
        """Uses the manually entered path to find folders."""
        new_path = self.path_entry.get().strip()
        if os.path.isdir(new_path):
            self.root_dir = new_path
            self.folders = sorted([d for d in os.listdir(self.root_dir) if os.path.isdir(os.path.join(self.root_dir, d))])
            self.update_folder_menu()
            self.select_root_button.config(text=f"Using: {self.root_dir}", state="normal")
        else:
            self.select_root_button.config(text="Invalid Path", state="normal")
            print(f"Error: The path '{new_path}' is not a valid directory.")

    def auto_locate_jsons_folder(self):
        """Automatically finds the JSONs folder and populates the UI."""
        script_dir = os.path.dirname(os.path.abspath(__file__))
        self.root_dir = os.path.join(script_dir, "JSONs")

        if os.path.isdir(self.root_dir):
            self.folders = sorted([d for d in os.listdir(self.root_dir) if os.path.isdir(os.path.join(self.root_dir, d))])
            self.update_folder_menu()
            self.select_root_button.config(text=f"Found: {self.root_dir}", state="normal")
        else:
            self.select_root_button.config(text=f"JSONs folder not found at: {self.root_dir}", state="normal")
            print("Error: 'JSONs' folder not found. Please ensure it is in the same directory as the executable.")

    def update_folder_menu(self):
        """Updates the folder dropdown with subfolders (dinosaur names) from the root directory."""
        menu = self.folder_menu["menu"]
        menu.delete(0, "end")
        if not self.folders:
            menu.add_command(label="No Dinosaurs found", command=lambda: 0)
            self.folder_var.set("No Dinosaurs found")
        else:
            for folder in self.folders:
                menu.add_command(label=folder, command=lambda value=folder: self.folder_var.set(value))
            self.folder_var.set(self.folders[0])

    def update_json_files(self, *args):
        """
        Updates the JSON file dropdown based on the selected dinosaur folder,
        including dynamically generated "virtual" attack files.
        """
        selected_dino_folder = self.folder_var.get()
        self.json_files_paths = {}
        self.virtual_files_data = {}
        
        if not selected_dino_folder or selected_dino_folder == "No Dinosaurs found":
            self.json_files_paths = {}
        else:
            attributes_path = os.path.join(self.root_dir, selected_dino_folder, "Attributes")
            if os.path.isdir(attributes_path):
                # Get the BalanceAttributes file from the root folder
                balance_file_path = os.path.join(self.root_dir, selected_dino_folder, f"DT_{selected_dino_folder}BalanceAttributes.json")
                if os.path.exists(balance_file_path):
                    self.json_files_paths[format_file_name(balance_file_path, selected_dino_folder)] = balance_file_path

                # Get the plottable files from the Attributes folder
                for file_path in glob.glob(os.path.join(attributes_path, "ATT_*.json")):
                    try:
                        file_data = get_json_data(file_path)
                        if file_data and ("FloatCurves" in file_data[0] or ("Properties" in file_data[0] and "FloatCurve" in file_data[0]["Properties"])):
                            if not is_linear_or_flat(file_path):
                                self.json_files_paths[format_file_name(file_path, selected_dino_folder)] = file_path
                    except (KeyError, IndexError):
                        continue # Skip malformed or irrelevant files

                self.generate_virtual_attack_files(selected_dino_folder)
            else:
                self.json_files_paths = {}
                print(f"Error: 'Attributes' folder not found inside {selected_dino_folder}.")

        menu = self.json_menu["menu"]
        menu.delete(0, "end")
        all_display_names = sorted(list(self.json_files_paths.keys()) + list(self.virtual_files_data.keys()))

        if not all_display_names:
            menu.add_command(label="No files found", command=lambda: 0)
            self.json_file_var.set("No files found")
            self.plot_button.config(state="disabled")
        else:
            for name in all_display_names:
                menu.add_command(label=name, command=lambda value=name: self.json_file_var.set(value))
            self.json_file_var.set(all_display_names[0])
            self.plot_button.config(state="normal")
    
    def generate_virtual_attack_files(self, dinosaur_name):
        """Generates virtual JSON data for attack graphs."""
        
        balance_file_path = os.path.join(self.root_dir, dinosaur_name, f"DT_{dinosaur_name}BalanceAttributes.json")
        attack_power_file_path = os.path.join(self.root_dir, dinosaur_name, "Attributes", f"ATT_{dinosaur_name}_AttackPower.json")

        balance_data = get_json_data(balance_file_path)
        attack_power_data = get_json_data(attack_power_file_path)

        if not balance_data or not attack_power_data:
            print(f"Warning: Could not find required files for {dinosaur_name} to generate virtual attacks.")
            return
            
        try:
            ap_curves = []
            ap_content = attack_power_data[0]
            if "FloatCurves" in ap_content:
                ap_curves = ap_content["FloatCurves"]
            elif "Properties" in ap_content and "FloatCurve" in ap_content["Properties"]:
                ap_curves = [ap_content["Properties"]["FloatCurve"]]
            else:
                raise ValueError("AttackPower file has an unexpected format.")
        except (KeyError, ValueError, IndexError) as e:
            print(f"Error parsing AttackPower data: {e}")
            return

        damage_attributes = {k: v["AttributePercentageValues"] for k, v in balance_data[0]["Rows"].items() if k.startswith("Damage.") and v["AttributePercentageValues"] != 0}
        
        for attack_name, damage_value in damage_attributes.items():
            clean_attack_name = attack_name.split('.')[-1]
            formatted_name = format_virtual_name(clean_attack_name)
            display_name = f"{formatted_name} Attack"
            
            # Generate the new curves for each life stage
            new_curves = []
            # Plot only the first two curves, assuming Senior and Elder
            for curve in ap_curves[:2]:
                time_points = []
                values = []
                for key in curve["Keys"]:
                    if key["Time"] > 0.0:
                        time_points.append(key["Time"])
                        values.append(key["Value"] * damage_value)
                new_curves.append({"Time": time_points, "Values": values})
            
            # Store the data and its label
            self.virtual_files_data[display_name] = {
                "curves": new_curves,
                "y_label": "Damage",
                "title_name": display_name
            }
        
    def enable_plot_button(self, *args):
        """Enables the plot button only if a valid file is selected."""
        selected_file_name = self.json_file_var.get()
        if selected_file_name and selected_file_name != "No files found":
            if "Balance Attributes" in selected_file_name:
                self.plot_button.config(text="Show Data Table", state="normal", command=self.show_selected_data_table)
            else:
                self.plot_button.config(text="Plot Data", state="normal", command=self.plot_selected_file)
        else:
            self.plot_button.config(state="disabled")

    def show_selected_data_table(self):
        selected_file_name = self.json_file_var.get()
        selected_dino_folder = self.folder_var.get()
        full_path = self.json_files_paths.get(selected_file_name)
        if full_path:
            show_balance_table(full_path)

    def plot_selected_file(self):
        """Calls the plotting function with the selected file path."""
        selected_file_name = self.json_file_var.get()
        
        if selected_file_name in self.virtual_files_data:
            data = self.virtual_files_data[selected_file_name]
            curves_data = data["curves"]
            y_label = data["y_label"]
            title_name = data["title_name"]
            
            time_points_list = [curve["Time"] for curve in curves_data]
            values_list = [curve["Values"] for curve in curves_data]

            plot_data(time_points_list, values_list, title_name, y_label)
        else:
            # Handle regular files from disk
            selected_dino_folder = self.folder_var.get()
            full_path = self.json_files_paths.get(selected_file_name)
            if full_path:
                file_data = get_json_data(full_path)
                
                if file_data:
                    float_curves = file_data[0].get("FloatCurves") or [file_data[0].get("Properties", {}).get("FloatCurve")]
                    if float_curves and float_curves[0]:
                        time_points_list = []
                        values_list = []
                        for i, curve in enumerate(float_curves[:2]):
                            time_points = [key["Time"] for key in curve["Keys"]]
                            values = [key["Value"] for key in curve["Keys"]]
                            
                            # Check for the specific weight and scale curve conditions
                            if selected_file_name.lower() in ["weight", "scale"]:
                                if time_points and time_points[-1] < 1.0:
                                    last_value = values[-1]
                                    time_points.append(1.0)
                                    values.append(last_value)

                            y_label = "Value"
                            conversion_factor = 1.0
                            if "speed" in selected_file_name.lower():
                                y_label = "Value (km/h)"
                                conversion_factor = 0.036
                            elif "weight" in selected_file_name.lower():
                                y_label = "Value (kg)"
                            
                            values = [v * conversion_factor for v in values]
                            time_points_list.append(time_points)
                            values_list.append(values)
                        
                        plot_data(time_points_list, values_list, selected_file_name, y_label)

# Run the UI
if __name__ == "__main__":
    root = Tk()
    app = JSONPlotterUI(root)
    root.mainloop()