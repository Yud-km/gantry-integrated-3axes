/*
 * limits.c - CNC Shield V3 active LOW limit switch and homing module
 *
 * X limit  = D9  / PB1
 * Y limit  = D10 / PB2
 * Z+ limit = D11 / PB3
 *
 * Active LOW:
 *   COM -> GND
 *   NO  -> CNC Shield limit input
 */


#include <avr/io.h>
#include <avr/interrupt.h>
#include <util/delay.h>
#include <stdint.h>

#include "cpu_map.h"
#include "limits.h"
#include "planner.h"
#include "stepper.h"
#include "serial.h"

/* Fallbacks if an older limits.h is still accidentally included. */
#ifndef LIMIT_X
#define LIMIT_X       bit(0)
#endif

#ifndef LIMIT_Y
#define LIMIT_Y       bit(1)
#endif

#ifndef LIMIT_Z_PLUS
#define LIMIT_Z_PLUS  bit(2)
#endif

#ifndef LIMIT_Z
#define LIMIT_Z       LIMIT_Z_PLUS
#endif

#ifndef HOMING_SEARCH_X_MM
#define HOMING_SEARCH_X_MM 500.0f
#endif

#ifndef HOMING_SEARCH_Y_MM
#define HOMING_SEARCH_Y_MM 500.0f
#endif

#ifndef HOMING_PULL_OFF_MM
#define HOMING_PULL_OFF_MM 5.0f
#endif

#ifndef HOMING_STEP_DELAY_US
#define HOMING_STEP_DELAY_US 900
#endif

static volatile uint8_t hard_limit_enabled = 0;
static volatile uint8_t hard_limit_alarm = 0;

static void limit_step_pulse(uint8_t step_bit, uint8_t dir_bit, uint8_t negative_dir)
{
    if (negative_dir) {
        DIRECTION_PORT |= (1 << dir_bit);
    } else {
        DIRECTION_PORT &= ~(1 << dir_bit);
    }

    STEP_PORT |= (1 << step_bit);
    _delay_us(8);
    STEP_PORT &= ~(1 << step_bit);
    _delay_us(HOMING_STEP_DELAY_US);
}

static void limit_move_mm(
    uint8_t step_bit,
    uint8_t dir_bit,
    uint8_t negative_dir,
    float mm,
    float steps_per_mm)
{
    uint32_t steps = (uint32_t)(mm * steps_per_mm);

    for (uint32_t i = 0; i < steps; i++) {
        limit_step_pulse(step_bit, dir_bit, negative_dir);
    }
}

void limits_init(void)
{
    LIMIT_DDR_B &= ~LIMIT_MASK_B;
    LIMIT_PORT_B |= LIMIT_MASK_B;

    hard_limit_alarm = 0;
    limits_enable();
}

void limits_enable(void)
{
    hard_limit_enabled = 1;

    PCMSK0 |= LIMIT_MASK_B;
    PCICR |= (1 << PCIE0);
}

void limits_disable(void)
{
    hard_limit_enabled = 0;

    PCMSK0 &= ~LIMIT_MASK_B;
}

static uint8_t read_limit_b(uint8_t bit)
{
    return (LIMIT_PIN_B & (1 << bit)) ? 0 : 1;
}

uint8_t limits_x(void)
{
    return read_limit_b(X_LIMIT_BIT);
}

uint8_t limits_y(void)
{
    return read_limit_b(Y_LIMIT_BIT);
}

uint8_t limits_z_plus(void)
{
    return read_limit_b(Z_PLUS_LIMIT_BIT);
}

uint8_t limits_z(void)
{
    return limits_z_plus();
}

uint8_t limits_x_min(void)
{
    return limits_x();
}

uint8_t limits_x_max(void)
{
    return limits_x();
}

uint8_t limits_y_min(void)
{
    return limits_y();
}

uint8_t limits_y_max(void)
{
    return limits_y();
}

uint8_t limits_get_state(void)
{
    uint8_t state = 0;

    if (limits_x()) {
        state |= LIMIT_X;
        state |= LIMIT_X_MIN;
        state |= LIMIT_X_MAX;
    }

    if (limits_y()) {
        state |= LIMIT_Y;
        state |= LIMIT_Y_MIN;
        state |= LIMIT_Y_MAX;
    }

    if (limits_z_plus()) {
        state |= LIMIT_Z_PLUS;
    }

    return state;
}

uint8_t limits_hard_alarm(void)
{
    return hard_limit_alarm;
}

void limits_clear_hard_alarm(void)
{
    hard_limit_alarm = 0;
}

ISR(PCINT0_vect)
{
    if (!hard_limit_enabled) {
        return;
    }

    uint8_t state = limits_get_state();

#if LIMIT_Z_AS_CONTACT_ONLY
    if (state & (LIMIT_X | LIMIT_Y)) {
        hard_limit_alarm = 1;
        st_go_idle();
    }
#else
    if (state & (LIMIT_X | LIMIT_Y | LIMIT_Z_PLUS)) {
        hard_limit_alarm = 1;
        st_go_idle();
    }
#endif
}

uint8_t limits_check_home(void)
{
    return (limits_x() && limits_y()) ? 1 : 0;
}

uint8_t limits_go_home(uint8_t cycle_mask)
{
    uint32_t max_x_steps =
        (uint32_t)(HOMING_SEARCH_X_MM * planner_settings.steps_per_mm[X_AXIS]);

    uint32_t max_y_steps =
        (uint32_t)(HOMING_SEARCH_Y_MM * planner_settings.steps_per_mm[Y_AXIS]);

    serial_write_ln("homing:start");

    hard_limit_alarm = 0;
    limits_disable();

    STEPPERS_DISABLE_PORT &= ~(1 << STEPPERS_DISABLE_BIT);

    if (cycle_mask & HOMING_CYCLE_X) {
        serial_write_ln("homing:x_search");

        uint32_t count = 0;

        while (!limits_x() && count < max_x_steps) {
            limit_step_pulse(X_STEP_BIT, X_DIRECTION_BIT, HOMING_X_DIR_NEGATIVE);
            count++;
        }

        if (!limits_x()) {
            serial_write_ln("homing:x_fail");
            limits_enable();
            return HOMING_FAIL_X;
        }

        serial_write_ln("homing:x_hit");
    }

    _delay_ms(200);

    if (cycle_mask & HOMING_CYCLE_Y) {
        serial_write_ln("homing:y_search");

        uint32_t count = 0;

        while (!limits_y() && count < max_y_steps) {
            limit_step_pulse(Y_STEP_BIT, Y_DIRECTION_BIT, HOMING_Y_DIR_NEGATIVE);
            count++;
        }

        if (!limits_y()) {
            serial_write_ln("homing:y_fail");
            limits_enable();
            return HOMING_FAIL_Y;
        }

        serial_write_ln("homing:y_hit");
    }

    if (!limits_check_home()) {
        serial_write_ln("homing:not_both_on");
        limits_enable();
        return HOMING_FAIL_Y;
    }

    serial_write_ln("homing:both_on_delay");
    _delay_ms(3000);

    serial_write_ln("homing:pull_off");

    if (cycle_mask & HOMING_CYCLE_X) {
        limit_move_mm(
            X_STEP_BIT,
            X_DIRECTION_BIT,
            !HOMING_X_DIR_NEGATIVE,
            HOMING_PULL_OFF_MM,
            planner_settings.steps_per_mm[X_AXIS]
        );
    }

    if (cycle_mask & HOMING_CYCLE_Y) {
        limit_move_mm(
            Y_STEP_BIT,
            Y_DIRECTION_BIT,
            !HOMING_Y_DIR_NEGATIVE,
            HOMING_PULL_OFF_MM,
            planner_settings.steps_per_mm[Y_AXIS]
        );
    }

    st_set_position_mm(0.0f, 0.0f, 0.0f);

    limits_enable();

    serial_write_ln("homing:ok");
    return HOMING_OK;
}
