/*
  serial.c - su dung ngat truyen nhan de giao tiep uart
*/

#include <avr/io.h>
#include <avr/interrupt.h>
#include <stdint.h>

#include "cpu_map.h"
#include "serial.h"


/*
khai bao bo dem vong
head - vi tri ghi du lieu moi
tail - vi tri doc du lieu ra
*/
static volatile uint8_t rx_buffer[RX_BUFFER_SIZE];
static volatile uint8_t rx_head = 0;
static volatile uint8_t rx_tail = 0;

static volatile uint8_t tx_buffer[TX_BUFFER_SIZE];
static volatile uint8_t tx_head = 0;
static volatile uint8_t tx_tail = 0;


/*
baudrate 115200 
cau hinh: 8 bit, no parity, 1 bit stop
ngat khi nhan duoc du lieu
*/
void serial_init(uint32_t baud)
{
	uint16_t ubrr;

	UCSR0A = (1 << U2X0);
	ubrr = (uint16_t)((F_CPU / 8UL / baud) - 1UL);

	UBRR0H = (uint8_t)(ubrr >> 8);
	UBRR0L = (uint8_t)ubrr;

	UCSR0B = (1 << RXEN0) | (1 << TXEN0) | (1 << RXCIE0);
	UCSR0C = (1 << UCSZ01) | (1 << UCSZ00);

	rx_head = 0;
	rx_tail = 0;
	tx_head = 0;
	tx_tail = 0;
}


uint8_t serial_available(void)
{
	if (rx_head >= rx_tail) return rx_head - rx_tail;
	return RX_BUFFER_SIZE - rx_tail + rx_head;
}



uint8_t serial_read(void)
{
	if (rx_head == rx_tail) return SERIAL_NO_DATA;

	uint8_t data = rx_buffer[rx_tail];

	rx_tail++;
	if (rx_tail == RX_BUFFER_SIZE) rx_tail = 0;

	return data;
}


/*
k truc tiep gui ngay, dua du lieu vao tx_buffer roi bat ngat
khi uart san sang gui byte tiep theo ngat nay se chay
*/
void serial_write(uint8_t data)
{
	uint8_t next_head = tx_head + 1;
	if (next_head == TX_BUFFER_SIZE) next_head = 0;

	while (next_head == tx_tail) {
		// Wait while TX buffer is full.
	}

	tx_buffer[tx_head] = data;
	tx_head = next_head;

	UCSR0B |= (1 << UDRIE0);
}


void serial_write_string(const char *s)
{
	while (*s) serial_write((uint8_t)*s++);
}


void serial_write_ln(const char *s)
{
	serial_write_string(s);
	serial_write('\r');
	serial_write('\n');
}


void serial_reset_read_buffer(void)
{
	rx_head = 0;
	rx_tail = 0;
}


/*
ngat nhan uart, khi winform gui 1 ky tu vis du P, I, N, G ngat nay tu chay
va bo ky tu vao rx_buffer
*/
ISR(USART_RX_vect)
{
	uint8_t data = UDR0;

	uint8_t next_head = rx_head + 1;
	if (next_head == RX_BUFFER_SIZE) next_head = 0;

	if (next_head != rx_tail) {
		rx_buffer[rx_head] = data;
		rx_head = next_head;
	}
}


/*
ngat truyen uart, chay khi uart sansang gui byte tiep theo
*/
ISR(USART_UDRE_vect)
{
	//neu k con du lieu tat ngat gui
	if (tx_head == tx_tail) {
		UCSR0B &= ~(1 << UDRIE0);
		} else {
		UDR0 = tx_buffer[tx_tail];

		tx_tail++;
		if (tx_tail == TX_BUFFER_SIZE) tx_tail = 0;
	}
}
