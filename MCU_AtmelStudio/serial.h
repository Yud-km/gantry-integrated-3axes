/*
  serial.h - UART ring buffer for WinForms communication
*/

#ifndef SERIAL_H
#define SERIAL_H

#include <stdint.h>

#ifndef RX_BUFFER_SIZE
#define RX_BUFFER_SIZE 128
#endif

#ifndef TX_BUFFER_SIZE
#define TX_BUFFER_SIZE 128
#endif

#define SERIAL_NO_DATA 0xFF

//khoi tao uart
void serial_init(uint32_t baud);

//ktra trong rx_buffer co bao nhieu byte dang cho doc
uint8_t serial_available(void);

//doc 1 ky tu 
uint8_t serial_read(void);

//ghi vao tx_buffer - sap xep du lieu vao buffer
void serial_write(uint8_t data);

//gui chuoi ky tu
void serial_write_string(const char *s);

void serial_write_ln(const char *s);

//
void serial_reset_read_buffer(void);

#endif
