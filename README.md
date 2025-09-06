# The-Isle-Stat-Reader

This is a Python script that reads stat graphs and stat files from *The Isle* to give the most accurate stat overview you can get.

---

## **Features**

- **Interactive Graphs**  
  Plots data curves with tooltips that display the exact values at each data point when you hover over them.

- **File Structure Awareness**  
  The tool automatically looks for a `JSONs` folder in the same directory and organizes the data by dinosaur species.

- **Data Table View**  
  For `BalanceAttributes` files, the tool presents the data in a clean, readable table instead of a plot.

- **Virtual Plots**  
  It can combine data from `BalanceAttributes` and `AttackPower` files to create "virtual" graphs that show the actual damage output of each attack type.

---

## **How to Build**

### **1. Prerequisites**

You need Python 3 installed on your system. The script also requires a few libraries that can be installed using pip:

```bash
pip install matplotlib
```

> ℹ️ The `tkinter` and `json` libraries are typically included with a standard Python installation.

---

### **2. Folder Structure**

The script is designed to work with a specific folder structure.  
You must place the script in the same directory as a folder named `JSONs`.

Inside the `JSONs` folder, you can extract these files from FModel under:

```
TheIsle/Content/TheIsle/Core/Characters/Dinosaurs
```

**Example structure:**

```
/Your-Project-Folder/
├── plotter.py
└── /JSONs/
    ├── /Allosaurus/
    │   ├── DT_AllosaurusBalanceAttributes.json
    │   └── /Attributes/
    │       ├── ATT_Allosaurus_AttackPower.json
    │       └── ATT_Allosaurus_RunSpeed.json
    └── /Pachycephalosaurus/
        ├── DT_PachycephalosaurusBalanceAttributes.json
        └── /Attributes/
            ├── ATT_Pachycephalosaurus_AttackPower.json
            └── ATT_Pachycephalosaurus_Stamina.json
```

> *(There is a lot more graphs not shown here)*

---

### **3. Running the Script**

Open your terminal or command prompt, navigate to the folder where you saved `UEJSONReader.py`, and run the script:

```bash
python UEJSONReader.py
```

The GUI window will appear automatically.

---

### **4. Using the GUI**

- **Select Dinosaur:**  
  Use the **"Select Dinosaur"** dropdown to choose a species.  
  The application will automatically populate the available files for that species.

- **Select Attribute:**  
  Use the **"Select Attribute"** dropdown to choose a specific JSON file to plot.

- **Plot Data:**
  - If you select a plotable attribute (like **Speed** or **Weight**), click **"Plot Data"** to generate an interactive graph.
  - If you select a **Balance Attributes** file, the button will change to **"Show Data Table"**. Click it to view the raw data in a separate window.

- **Override JSON Folder Location:**  
  Use the checkbox and entry field to manually set a different path — but **file structure must be the same**.

---
