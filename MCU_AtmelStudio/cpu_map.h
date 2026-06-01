/*
  cpu_map.h - Pin mapping for GRBL-like gantry crane project
  MCU: Arduino Uno R3 / ATmega328P
  Shield: CNC Shield V3 + 4 x A4988
  Axis: 1X, 2Y cloned by CNC shield jumper, 1Z

  Coordinate convention:
    Home cell 1 = X0 Y0
    If X moves +3 mm and Y moves +5 mm, coordinate = X3 Y5
*/

#ifndef CPU_MAP_H
#define CPU_MAP_H

#include <avr/io.h>

// ================= BASIC MACROS =================
#ifndef bit
#define bit(n) (1 << (n))
#endif

#ifndef F_CPU
#define F_CPU 16000000UL
#endif

// ================= SERIAL UART =================
#define SERIAL_RX_vect_NAME      USART_RX_vect
#define SERIAL_UDRE_vect_NAME    USART_UDRE_vect

// ================= STEPPER STEP PINS =================
// CNC Shield V3 standard:
// X_STEP = D2 / PD2
// Y_STEP = D3 / PD3
// Z_STEP = D4 / PD4
// Y2 motor driver is cloned from Y by CNC Shield V3 jumper.
// Therefore software only outputs one Y STEP/DIR signal.

#define STEP_DDR        DDRD
#define STEP_PORT       PORTD
#define STEP_PIN        PIND

#define X_STEP_BIT      2   // Arduino D2
#define Y_STEP_BIT      3   // Arduino D3
#define Z_STEP_BIT      4   // Arduino D4

#define STEP_MASK       ((1 << X_STEP_BIT) | (1 << Y_STEP_BIT) | (1 << Z_STEP_BIT))

// ================= STEPPER DIRECTION PINS =================
// X_DIR = D5 / PD5
// Y_DIR = D6 / PD6
// Z_DIR = D7 / PD7

#define DIRECTION_DDR       DDRD
#define DIRECTION_PORT      PORTD
#define DIRECTION_PIN       PIND

#define X_DIRECTION_BIT     5   // Arduino D5
#define Y_DIRECTION_BIT     6   // Arduino D6
#define Z_DIRECTION_BIT     7   // Arduino D7

#define DIRECTION_MASK      ((1 << X_DIRECTION_BIT) | (1 << Y_DIRECTION_BIT) | (1 << Z_DIRECTION_BIT))

// ================= STEPPER ENABLE PIN =================
// CNC Shield V3 EN = D8 / PB0
// A4988 enable is normally active LOW.

#define STEPPERS_DISABLE_DDR    DDRB
#define STEPPERS_DISABLE_PORT   PORTB
#define STEPPERS_DISABLE_BIT    0   // Arduino D8
#define STEPPERS_DISABLE_MASK   (1 << STEPPERS_DISABLE_BIT)

// ================= LIMIT SWITCHES =================
 /* CNC Shield V3 actual limit input pins:
 *   X limit = D9  / PB1
 *   Y limit = D10 / PB2
 *   Z+ limit = D11 / PB3
 *
 * X+ and X- share X_LIMIT_BIT.
 * Y+ and Y- share Y_LIMIT_BIT.
 * Only Z+ is connected to Z_LIMIT_BIT.
 *
 * Active LOW:
 *   COM -> GND
 *   NO  -> limit input
 */

#define LIMIT_DDR_B       DDRB
#define LIMIT_PIN_B       PINB
#define LIMIT_PORT_B      PORTB

#define X_LIMIT_BIT       1   /* D9  */
#define Y_LIMIT_BIT       2   /* D10 */
#define Z_LIMIT_BIT       3   /* D11 */

#define Z_PLUS_LIMIT_BIT  Z_LIMIT_BIT

#define X_MIN_LIMIT_BIT   X_LIMIT_BIT
#define X_MAX_LIMIT_BIT   X_LIMIT_BIT

#define Y_MIN_LIMIT_BIT   Y_LIMIT_BIT
#define Y_MAX_LIMIT_BIT   Y_LIMIT_BIT

#define Z_MIN_LIMIT_BIT   Z_LIMIT_BIT
#define Z_MAX_LIMIT_BIT   Z_LIMIT_BIT

#define LIMIT_MASK_B      ((1 << X_LIMIT_BIT) | (1 << Y_LIMIT_BIT) | (1 << Z_LIMIT_BIT))

#define LIMIT_DDR_C       DDRC
#define LIMIT_PIN_C       PINC
#define LIMIT_PORT_C      PORTC
#define LIMIT_MASK_C      0

#define LIMIT_Z_AS_CONTACT_ONLY 0

// ================= PHOTO SENSOR =================
// NPN photoelectric sensor 3FE-DS304C
// OUT -> A2 / PC2
// Active LOW assumption.

#define SENSOR_DDR        DDRC
#define SENSOR_PIN        PINC
#define SENSOR_PORT       PORTC
#define PHOTO_SENSOR_BIT  2   // A2 - resume
#define PHOTO_SENSOR_MASK (1 << PHOTO_SENSOR_BIT)

// ================= RELAY OUTPUT =================
// Relay channel 1: Fan
// Relay channel 2: Electromagnet
// A0 / PC0 and A1 / PC1 are free because LCD uses I2C A4/A5.

#define RELAY_DDR         DDRC
#define RELAY_PORT        PORTC

#define FAN_RELAY_BIT     0   // A0 - abort
#define MAGNET_RELAY_BIT  1   // A1 - hold
#define RELAY_MASK        ((1 << FAN_RELAY_BIT) | (1 << MAGNET_RELAY_BIT))

// Set 1 if your relay module is active LOW.
#define RELAY_ACTIVE_LOW  0

// ================= LCD I2C =================
// LCD1602 I2C module.
// SDA = A4 / PC4
// SCL = A5 / PC5

#define LCD_I2C_SDA_BIT   4
#define LCD_I2C_SCL_BIT   5
#define LCD_I2C_ADDR      0x27

// ================= HOMING DIRECTION =================
// Change these if motor direction is opposite.
// 1 means move in negative direction to find min switch.
// 0 means move in positive direction.

#define HOMING_X_DIR_NEGATIVE  1
#define HOMING_Y_DIR_NEGATIVE  1

// ================= MACHINE DEFAULTS =================
#define DEFAULT_Z_SAFE_MM       30.0f

#endif
