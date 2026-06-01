/*
  grbl.h - Main include file for GRBL-like gantry crane firmware

  Project:
    Arduino Uno R3 / ATmega328P
    CNC Shield V3
    4 x A4988
    1 X motor, 2 Y motors cloned by shield jumper, 1 Z motor
    Photo sensor NPN 3FE-DS304C
    2-channel relay: fan + electromagnet
    LCD1602 I2C
    UART communication with WinForms

  This file follows the same role as original GRBL grbl.h:
    - define firmware version
    - include standard AVR libraries
    - include all project modules in one place
    - check important compile-time configuration values

  Coordinate convention:
    Home cell 1 = X0 Y0
    If X moves +3 mm and Y moves +5 mm, coordinate = X3 Y5
*/

#ifndef GRBL_H
#define GRBL_H

// ============================================================================
// Firmware version
// ============================================================================

#define GANTRY_GRBL_VERSION       "1.0.0"
#define GANTRY_GRBL_VERSION_BUILD "20260521"

#define GANTRY_PROJECT_NAME       "GRBL-like Gantry Crane"
#define GANTRY_MCU_NAME           "ATmega328P"
#define GANTRY_BOARD_NAME         "Arduino Uno R3 + CNC Shield V3"

// ============================================================================
// Standard AVR / C libraries
// ============================================================================

#ifndef F_CPU
#define F_CPU 16000000UL
#endif

/* AVR / C libraries */
#include <avr/io.h>
#include <avr/interrupt.h>
#include <avr/pgmspace.h>
#include <util/delay.h>

#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <math.h>

// ============================================================================
// Common macros
// ============================================================================
/*
#ifndef bit
#define bit(n) (1 << (n))
#endif

#ifndef true
#define true 1
#endif

#ifndef false
#define false 0
#endif

#define GANTRY_OK      0
#define GANTRY_ERROR   1

#define GANTRY_AXIS_X  0
#define GANTRY_AXIS_Y  1
#define GANTRY_AXIS_Z  2
*/

// Expected values:
// X/Y = about 42.44 step/mm
// Z   = 400 step/mm

// ============================================================================
// Project module includes
// ============================================================================
//
// Include order is important:
// 1. cpu_map.h defines pin mapping and hardware macros.
// 2. planner.h defines N_AXIS and motion block structures.
// 3. stepper/system/limits/motion modules use planner and cpu_map.
// 4. peripheral modules are included after hardware mapping.
// ============================================================================

/* Project modules */
#include "cpu_map.h"
#include "serial.h"
#include "planner.h"
#include "stepper.h"
#include "limits.h"
#include "sensor.h"
#include "relay_control.h"
#include "lcd.h"

/* Public gantry application API */
#include "motion_control.h"
#endif 

/*
// ============================================================================
// Compile-time configuration checks
// ============================================================================

// Axis count check.
#ifndef N_AXIS
  #error "N_AXIS is not defined. Include planner.h before dependent modules."
#endif

#if (N_AXIS != 3)
  #error "This gantry crane firmware requires N_AXIS == 3."
#endif

// CPU clock check.
#if (F_CPU != 16000000UL)
  #warning "This firmware is tuned for Arduino Uno 16MHz. Check timer settings if F_CPU differs."
#endif

// Required pin map checks.
#ifndef STEP_DDR
  #error "STEP_DDR is not defined in cpu_map.h."
#endif

#ifndef STEP_PORT
  #error "STEP_PORT is not defined in cpu_map.h."
#endif

#ifndef DIRECTION_DDR
  #error "DIRECTION_DDR is not defined in cpu_map.h."
#endif

#ifndef DIRECTION_PORT
  #error "DIRECTION_PORT is not defined in cpu_map.h."
#endif

#ifndef X_STEP_BIT
  #error "X_STEP_BIT is not defined in cpu_map.h."
#endif

#ifndef Y_STEP_BIT
  #error "Y_STEP_BIT is not defined in cpu_map.h."
#endif

#ifndef Z_STEP_BIT
  #error "Z_STEP_BIT is not defined in cpu_map.h."
#endif

#ifndef X_DIRECTION_BIT
  #error "X_DIRECTION_BIT is not defined in cpu_map.h."
#endif

#ifndef Y_DIRECTION_BIT
  #error "Y_DIRECTION_BIT is not defined in cpu_map.h."
#endif

#ifndef Z_DIRECTION_BIT
  #error "Z_DIRECTION_BIT is not defined in cpu_map.h."
#endif

#ifndef STEPPERS_DISABLE_BIT
  #error "STEPPERS_DISABLE_BIT is not defined in cpu_map.h."
#endif

// Limit switch checks.
#ifndef X_MIN_LIMIT_BIT
  #error "X_MIN_LIMIT_BIT is not defined in cpu_map.h."
#endif

#ifndef X_MAX_LIMIT_BIT
  #error "X_MAX_LIMIT_BIT is not defined in cpu_map.h."
#endif

#ifndef Y_MIN_LIMIT_BIT
  #error "Y_MIN_LIMIT_BIT is not defined in cpu_map.h."
#endif

#ifndef Y_MAX_LIMIT_BIT
  #error "Y_MAX_LIMIT_BIT is not defined in cpu_map.h."
#endif

#ifndef Z_LIMIT_BIT
  #error "Z_LIMIT_BIT is not defined in cpu_map.h."
#endif

// Sensor / relay / LCD checks.
#ifndef PHOTO_SENSOR_BIT
  #error "PHOTO_SENSOR_BIT is not defined in cpu_map.h."
#endif

#ifndef FAN_RELAY_BIT
  #error "FAN_RELAY_BIT is not defined in cpu_map.h."
#endif

#ifndef MAGNET_RELAY_BIT
  #error "MAGNET_RELAY_BIT is not defined in cpu_map.h."
#endif

#ifndef LCD_I2C_ADDR
  #error "LCD_I2C_ADDR is not defined in cpu_map.h."
#endif

// Relay logic check.
#ifndef RELAY_ACTIVE_LOW
  #error "RELAY_ACTIVE_LOW must be defined as 0 or 1 in cpu_map.h."
#endif

#if (RELAY_ACTIVE_LOW != 0) && (RELAY_ACTIVE_LOW != 1)
  #error "RELAY_ACTIVE_LOW must be 0 or 1."
#endif

// Z switch behavior check.
#ifndef LIMIT_Z_AS_CONTACT_ONLY
  #error "LIMIT_Z_AS_CONTACT_ONLY must be defined as 0 or 1 in cpu_map.h."
#endif

#if (LIMIT_Z_AS_CONTACT_ONLY != 0) && (LIMIT_Z_AS_CONTACT_ONLY != 1)
  #error "LIMIT_Z_AS_CONTACT_ONLY must be 0 or 1."
#endif

// Homing direction check.
#ifndef HOMING_X_DIR_NEGATIVE
  #error "HOMING_X_DIR_NEGATIVE must be defined in cpu_map.h."
#endif

#ifndef HOMING_Y_DIR_NEGATIVE
  #error "HOMING_Y_DIR_NEGATIVE must be defined in cpu_map.h."
#endif

#if (HOMING_X_DIR_NEGATIVE != 0) && (HOMING_X_DIR_NEGATIVE != 1)
  #error "HOMING_X_DIR_NEGATIVE must be 0 or 1."
#endif

#if (HOMING_Y_DIR_NEGATIVE != 0) && (HOMING_Y_DIR_NEGATIVE != 1)
  #error "HOMING_Y_DIR_NEGATIVE must be 0 or 1."
#endif

// Pin conflict checks.
// These checks are simple same-port bit checks for project-critical pins.

// STEP and DIR are all on PORTD in CNC Shield V3.
// They must not overlap.
#if ((STEP_MASK & DIRECTION_MASK) != 0)
  #error "STEP pins and DIRECTION pins overlap. Check cpu_map.h."
#endif

// Relay and sensor are on PORTC. They must not overlap.
#if ((RELAY_MASK & PHOTO_SENSOR_MASK) != 0)
  #error "Relay pins and photo sensor pin overlap. Check cpu_map.h."
#endif

// Z limit/contact and sensor are both on PORTC. They must not overlap.
#if ((LIMIT_MASK_C & PHOTO_SENSOR_MASK) != 0)
  #error "Z limit/contact pin and photo sensor pin overlap. Check cpu_map.h."
#endif

// Relay and Z limit/contact are on PORTC. They must not overlap.
#if ((RELAY_MASK & LIMIT_MASK_C) != 0)
  #error "Relay pins and Z limit/contact pin overlap. Check cpu_map.h."
#endif

// LCD I2C should remain A4/A5. Relay/sensor/Z limit should not use A4/A5.
#if defined(LCD_I2C_SDA_BIT) && defined(LCD_I2C_SCL_BIT)
  #if ((RELAY_MASK & ((1 << LCD_I2C_SDA_BIT) | (1 << LCD_I2C_SCL_BIT))) != 0)
    #error "Relay pins conflict with LCD I2C A4/A5."
  #endif

  #if ((PHOTO_SENSOR_MASK & ((1 << LCD_I2C_SDA_BIT) | (1 << LCD_I2C_SCL_BIT))) != 0)
    #error "Photo sensor pin conflicts with LCD I2C A4/A5."
  #endif

  #if ((LIMIT_MASK_C & ((1 << LCD_I2C_SDA_BIT) | (1 << LCD_I2C_SCL_BIT))) != 0)
    #error "Z limit/contact pin conflicts with LCD I2C A4/A5."
  #endif
#endif
*/

