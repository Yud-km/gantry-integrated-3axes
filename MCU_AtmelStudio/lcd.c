/*
  lcd.c - Minimal LCD1602 I2C driver using ATmega328P TWI registers

  PCF8574 common mapping:
    P0 RS
    P1 RW
    P2 EN
    P3 Backlight
    P4-P7 data
*/

#include <avr/io.h>
#include <util/delay.h>
#include <stdint.h>

#include "cpu_map.h"
#include "lcd.h"

#define LCD_BACKLIGHT 0x08
#define LCD_ENABLE    0x04
#define LCD_RS        0x01

static void twi_init(void)
{
	TWSR = 0x00;
	TWBR = (uint8_t)(((F_CPU / 100000UL) - 16UL) / 2UL);
	TWCR = (1 << TWEN);
}

static void twi_start(void)
{
	TWCR = (1 << TWINT) | (1 << TWSTA) | (1 << TWEN);
	while (!(TWCR & (1 << TWINT))) {
	}
}

static void twi_stop(void)
{
	TWCR = (1 << TWINT) | (1 << TWSTO) | (1 << TWEN);
}

static void twi_write(uint8_t data)
{
	TWDR = data;
	TWCR = (1 << TWINT) | (1 << TWEN);
	while (!(TWCR & (1 << TWINT))) {
	}
}

static void lcd_expander_write(uint8_t data)
{
	twi_start();
	twi_write((LCD_I2C_ADDR << 1) | 0);
	twi_write(data | LCD_BACKLIGHT);
	twi_stop();
}

static void lcd_pulse_enable(uint8_t data)
{
	lcd_expander_write(data | LCD_ENABLE);
	_delay_us(1);
	lcd_expander_write(data & ~LCD_ENABLE);
	_delay_us(50);
}

static void lcd_write4(uint8_t nibble, uint8_t mode)
{
	uint8_t data = (nibble & 0xF0) | mode;
	lcd_expander_write(data);
	lcd_pulse_enable(data);
}

static void lcd_send(uint8_t value, uint8_t mode)
{
	lcd_write4(value & 0xF0, mode);
	lcd_write4((value << 4) & 0xF0, mode);
}

static void lcd_command(uint8_t cmd)
{
	lcd_send(cmd, 0);
}

static void lcd_data(uint8_t data)
{
	lcd_send(data, LCD_RS);
}

void lcd_init(void)
{
	twi_init();

	_delay_ms(50);

	lcd_write4(0x30, 0);
	_delay_ms(5);
	lcd_write4(0x30, 0);
	_delay_us(150);
	lcd_write4(0x30, 0);
	lcd_write4(0x20, 0);

	lcd_command(0x28);
	lcd_command(0x0C);
	lcd_command(0x06);
	lcd_clear();
}

void lcd_clear(void)
{
	lcd_command(0x01);
	_delay_ms(2);
}

void lcd_home(void)
{
	lcd_command(0x02);
	_delay_ms(2);
}

void lcd_set_cursor(uint8_t col, uint8_t row)
{
	static const uint8_t row_offsets[] = {0x00, 0x40};
	if (row > 1) row = 1;
	lcd_command(0x80 | (col + row_offsets[row]));
}

void lcd_print(const char *s)
{
	while (*s) {
		lcd_data((uint8_t)*s++);
	}
}

void lcd_print_state(const char *state)
{
	lcd_clear();
	lcd_set_cursor(0, 0);
	lcd_print("Gantry Crane");
	lcd_set_cursor(0, 1);
	lcd_print(state);
}

