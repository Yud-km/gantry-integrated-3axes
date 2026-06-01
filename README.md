# GRBL-like Gantry Crane Controller

Firmware and WinForms control software for a small **3-axis gantry crane / (cầu trục)** based on Arduino Uno R3, CNC Shield V3, A4988 drivers, UART communication, LCD1602 I2C, photoelectric sensor, fan relay and electromagnet relay.

> Project status: working prototype. The current control logic is implemented in `motion_control.c`.

---

## Table of Contents

- [Overview](#overview)
- [Main Features](#main-features)
- [Hardware](#hardware)
- [Coordinate System](#coordinate-system)
- [Project Structure](#project-structure)
- [Firmware Modules](#firmware-modules)
- [Control Workflow](#control-workflow)
- [UART Commands](#uart-commands)
- [WinForms Application](#winforms-application)
- [Build and Upload](#build-and-upload)
- [Safety Notes](#safety-notes)
- [Troubleshooting](#troubleshooting)
- [License](#license)

---

## Overview

This project controls a gantry crane with:

- **X axis**: horizontal movement.
- **Y axis**: vertical movement on screen/working plane.
- **Z axis**: pickup/release movement.
- **Photo sensor**: detects object at HOME.
- **Electromagnet relay**: holds/release object.
- **Fan relay**: runs after releasing object at END.
- **LCD1602 I2C**: displays machine state, route, position and count.
- **WinForms UI**: sends commands and displays status through Arduino USB Serial.

The firmware uses a GRBL-like structure:

```text
main.c
└── calls gantry_init()
└── repeatedly calls gantry_loop_once()

motion_control.c
└── contains the main crane logic/state machine

planner.c / stepper.c
└── generate coordinated stepper motion
```

---

## Main Features

- Check Home from WinForms button.
- Jog X/Y/Z manually.
- GOTO absolute coordinate from WinForms.
- AUTO mode with sensor-triggered cycle.
- Set coordinates for 6 cells.
- Select route/path cells from WinForms.
- Save/load route profiles from WinForms `.txt` files.
- Display current route name on LCD.
- STOP behavior protects electromagnet state.
- Limit protection for X/Y/Z.
- UART status reports for WinForms UI.
- Product count increases once per completed cycle.

---

## Hardware

| Part | Description |
|---|---|
| MCU | Arduino Uno R3 / ATmega328P |
| Shield | CNC Shield V3 |
| Drivers | A4988 |
| Motors | X, Y, Z stepper motors |
| Y axis | Y and A can run together if A is cloned to Y by CNC Shield jumper |
| Sensor | Photoelectric sensor E3F-DS30C4 NPN |
| Relay | 2-channel relay: fan + electromagnet |
| Display | LCD1602 with I2C module |
| PC app | WinForms over USB Serial |

---

## Coordinate System

Default cells:

| Cell | X | Y | Z | Required | Default selected |
|---|---:|---:|---:|---:|---:|
| HOME | 0 | 0 | 0 | Yes | Yes |
| 1 | 135 | 0 | 0 | No | Yes |
| 2 | 270 | 0 | 0 | No | Yes |
| 3 | 270 | 270 | 0 | No | Yes |
| 4 | 135 | 270 | 0 | No | Yes |
| END | 0 | 270 | 0 | Yes | Yes |

Default route:

```text
HOME -> 1 -> 2 -> 3 -> 4 -> END -> HOME
```

---

## Project Structure

Recommended repository structure:

```text
Gantry_Integrated_Ver1.0/
├── MCU_AtmelStudio/
│   ├── main.c
│   ├── grbl.h
│   ├── motion_control.c
│   ├── motion_control.h
│   ├── cpu_map.h
│   ├── serial.c
│   ├── serial.h
│   ├── planner.c
│   ├── planner.h
│   ├── stepper.c
│   ├── stepper.h
│   ├── limits.c
│   ├── limits.h
│   ├── sensor.c
│   ├── sensor.h
│   ├── relay_control.c
│   ├── relay_control.h
│   ├── lcd.c
│   └── lcd.h
│
├── WinForms_GantryIntegrated/
│   ├── MainForm.cs
│   ├── Program.cs
│   ├── GantryCraneIntegrated.csproj
│   └── trajectory/
│       ├── Default.txt
│       ├── blue.txt
│       └── red.txt
│
└── README.md
```

---

## Firmware Modules

| File | Role |
|---|---|
| `main.c` | Short entry point. Calls `gantry_init()` and `gantry_loop_once()`. |
| `grbl.h` | Common include file for AVR libraries and project modules. |
| `motion_control.c` | Main crane logic: homing, jog, auto, route, LCD, UART, relay, limit. |
| `motion_control.h` | Public interface for motion control module. |
| `cpu_map.h` | Pin mapping for Arduino Uno + CNC Shield V3. |
| `planner.c/h` | Motion planner. |
| `stepper.c/h` | Step pulse generation. |
| `limits.c/h` | Limit switch input handling. |
| `sensor.c/h` | Photo sensor input. |
| `relay_control.c/h` | Fan/electromagnet relay control. |
| `lcd.c/h` | LCD1602 I2C display. |
| `serial.c/h` | UART communication with WinForms. |

---

## Control Workflow

### 1. Check Home

Triggered by WinForms `CHECK HOME` button.

```text
HOME command
-> X- search until X limit active
-> Y- search until Y limit active
-> wait 3 seconds
-> pull off X/Y by 5 mm
-> set current coordinate = X0 Y0 Z0
-> report home:ok
```

### 2. AUTO Cycle

```text
START AUTO
-> machine stays Idle at HOME, waiting for object
-> sensor detects object
-> Z+ runs until Z+ limit active
-> magnet ON
-> Z returns to 0
-> move through selected route cells
-> at each middle cell: Z+ to configured travel, delay 3s, Z back to 0
-> at END: Z+ until limit, magnet OFF, fan ON
-> pull Z off limit
-> return HOME
-> fan runs 5 seconds
-> count +1
-> wait for next object
```

### 3. Route Change While Idle

When the crane has returned HOME and is waiting for a new object, the state is `Idle`. In this state the WinForms ComboBox can change the route.

```text
Select route in ComboBox
-> WinForms sends ROUTE + CELL + PATH
-> MCU updates route immediately
-> no need to press START AUTO again
-> next sensor trigger uses the new route
```

---

## UART Commands

| Command | Description |
|---|---|
| `HOME` | Start check home sequence. |
| `AUTO` | Arm AUTO mode. |
| `STOP` | Stop motion and fan. Magnet remains ON if holding object. |
| `CLEAR` | Clear alarm. |
| `STATUS` | Request full status packet. |
| `LIMITS` | Request limit states. |
| `CELLS` | Request all cell data. |
| `CELL <name> X... Y... Z...` | Set cell coordinate. |
| `PATH <name> ON/OFF` | Select/unselect a route cell. |
| `ROUTE <name>` | Set route name for LCD/status. |
| `GOTO X... Y... Z... F...` | Move to absolute coordinate. |
| `X+`, `X-`, `Y+`, `Y-`, `Z+`, `Z-` | Jog one step. |
| `STEP <value>` | Set jog step in mm. |
| `FEED <value>` | Set XY speed. |
| `FEEDZ <value>` | Set Z speed. |
| `FANON`, `FANOFF` | Manual fan relay control. |
| `MAGON`, `MAGOFF` | Manual magnet relay control. |
| `COUNTRESET` | Reset product count. |

Example:

```text
ROUTE blue
CELL 1 X135 Y0 Z0
PATH 1 ON
PATH 2 OFF
AUTO
```

---

## WinForms Application

Main UI functions:

- Connect/disconnect COM port.
- Select route profile from ComboBox.
- Check Home.
- Start Auto.
- Stop.
- Clear Alarm.
- Reset Count.
- Display sensor, limits, relay state, current coordinates and product count.
- Jog mode with X/Y/Z buttons.
- Route Setting page with route create/add/save/load.

Route profile files are saved as `.txt` in:

```text
WinForms_GantryIntegrated/trajectory/
```

Example route file:

```c
{"HOME", 0f, 0f, 0f, 1, 1},
{"1", 135f, 0f, 0f, 0, 1},
{"2", 270f, 0f, 0f, 0, 0},
{"3", 270f, 270f, 0f, 0, 1},
{"4", 135f, 270f, 0f, 0, 1},
{"END", 0f, 270f, 0f, 1, 1}
```

---

## Build and Upload

### Firmware

1. Open Atmel Studio.
2. Add all files in `MCU_AtmelStudio`.
3. Make sure `F_CPU = 16000000UL`.
4. Build project for ATmega328P.
5. Upload to Arduino Uno R3.

Required firmware files:

```text
main.c
grbl.h
motion_control.c / motion_control.h
cpu_map.h
serial.c / serial.h
planner.c / planner.h
stepper.c / stepper.h
limits.c / limits.h
sensor.c / sensor.h
relay_control.c / relay_control.h
lcd.c / lcd.h
```

### WinForms

1. Open the WinForms project in Visual Studio.
2. Build the project.
3. Run the application.
4. Select COM port and baudrate `115200`.
5. Press `CONNECT`.

---

## Safety Notes

- Always check motor direction before running AUTO.
- Test limit switches before enabling full motion.
- Use `STOP` if the crane behaves unexpectedly.
- The Z+ limit is used as a process/contact signal in AUTO.
- X/Y limits are safety limits outside homing.
- Do not change mechanical wiring while power is ON.
- If using two Y motors, make sure CNC Shield A-axis clone jumper is configured correctly.

---

## Troubleshooting

### No RX response in WinForms

- Check baudrate is `115200`.
- Make sure Serial Monitor is closed.
- Check correct COM port.
- Check line ending from WinForms is `\n`.

### AUTO does not start

- Make sure `CHECK HOME` has completed.
- Check sensor state on WinForms.
- Make sure machine is not in Alarm.
- Check selected route was sent to MCU.

### Limit alarm when returning HOME

- The firmware returns to coordinate `X0 Y0 Z0`.
- If X/Y limit is triggered before reaching that coordinate, it is treated as a real alarm.
- Re-check home offset and cell coordinates.

### Route is not updated

- Change route only when State is `Idle`.
- If machine is running, press `STOP` before changing route.
- Check WinForms log for `ROUTE`, `CELL`, and `PATH` commands.

---

## License

This project is for educational and prototype use.
