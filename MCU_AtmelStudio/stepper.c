/*
  stepper.c - GRBL-like stepper pulse generator

  Timer1 COMPA:
    - Raises STEP pins
    - Runs Bresenham interpolation for X/Y/Z
    - Updates real-time step position

  Timer0 OVF:
    - Drops STEP pins after short pulse width
*/

#include <avr/io.h>
#include <avr/interrupt.h>
#include <math.h>
#include <string.h>

#include "cpu_map.h"
#include "planner.h"
#include "stepper.h"

#ifndef MIN_STEP_RATE
#define MIN_STEP_RATE 60.0f
#endif

#ifndef MIN_ISR_CYCLES
#define MIN_ISR_CYCLES 100
#endif

//vi tri hien tai cua may - tinh bang step
//vi du: xet truc x = 50mm tao co truc x dung con lan 42.44step/mm
//=> sys_position[x] = 2122 step
volatile int32_t sys_position[N_AXIS];


/*
steps[]           = m?i tr?c c?n ch?y bao nhiêu step
step_event_count  = s? l?n interrupt chính c?n ch?y
counter[]         = b? ð?m n?i suy Bresenham
direction_bits    = chi?u quay t?ng tr?c
running           = ðang ch?y hay không
millimeters       = chi?u dài ðo?n ch?y tính b?ng mm
acceleration      = gia t?c
nominal_speed     = t?c ð? l?n nh?t ðo?n ðó
current_speed     = t?c ð? hi?n t?i
distance_done     = ð? ði ðý?c bao nhiêu mm
*/
typedef struct {
  uint32_t counter[N_AXIS];
  uint32_t steps[N_AXIS];
  uint32_t step_event_count;
  uint32_t step_count;

  uint8_t direction_bits;
  uint8_t dir_outbits;
  uint8_t running;

  float millimeters;
  float acceleration;
  float nominal_speed;
  float entry_speed;
  float exit_speed;
  float current_speed;
  float mm_per_step_event;
  float distance_done;
} stepper_t;


// trang thai hien tai cua stepper
static volatile stepper_t st;

static uint8_t step_port_invert_mask = 0;
static uint8_t dir_port_invert_mask = 0;

void st_generate_step_dir_invert_masks(void)
{
  // Add invert settings here if needed.
  step_port_invert_mask = 0;
  dir_port_invert_mask = 0;
}

static uint16_t speed_to_timer_cycles(float speed_mm_s)
{
  if (speed_mm_s < 0.1f) speed_mm_s = 0.1f;

  float steps_per_sec = speed_mm_s / st.mm_per_step_event;

  if (steps_per_sec < MIN_STEP_RATE) steps_per_sec = MIN_STEP_RATE;

  uint32_t cycles = (uint32_t)((float)F_CPU / steps_per_sec);

  if (cycles > 65535UL) cycles = 65535UL;
  if (cycles < MIN_ISR_CYCLES) cycles = MIN_ISR_CYCLES;

  return (uint16_t)cycles;
}


/*
1. B?t driver A4988
2. B?t ng?t Timer1 ð? b?t ð?u phát xung STEP
*/
void st_wake_up(void)
{
  // Enable A4988. CNC Shield enable is active LOW.
  STEPPERS_DISABLE_PORT &= ~(1 << STEPPERS_DISABLE_BIT);

  // Enable Timer1 compare interrupt.
  TIMSK1 |= (1 << OCIE1A);
}


/*
1. T?t ng?t Timer1
2. Disable driver A4988
3. Báo stepper không ch?y n?a
*/
void st_go_idle(void)
{
  TIMSK1 &= ~(1 << OCIE1A);
  
  //co the bo neu van muon giu luc cho step sau khi chay xong
  STEPPERS_DISABLE_PORT |= (1 << STEPPERS_DISABLE_BIT);
  st.running = 0;
}

uint8_t st_is_running(void)
{
  return st.running || !plan_is_buffer_empty();
}

void st_set_position_zero(void)
{
  st_set_position_mm(0.0f, 0.0f, 0.0f);
}


/*
ham nay chi co chuc nang bao voi he vi tri hien tai
k co the keo may ve dung vi tri 
*/
void st_set_position_mm(float x, float y, float z)
{
	//chuyen toa do mm sang step
  int32_t sx = lroundf(x * planner_settings.steps_per_mm[X_AXIS]);
  int32_t sy = lroundf(y * planner_settings.steps_per_mm[Y_AXIS]);
  int32_t sz = lroundf(z * planner_settings.steps_per_mm[Z_AXIS]);

  uint8_t sreg = SREG;
  cli();

	//gan vao bien luu toa do - step
  sys_position[X_AXIS] = sx;
  sys_position[Y_AXIS] = sy;
  sys_position[Z_AXIS] = sz;

  SREG = sreg;

  plan_set_position_steps(sx, sy, sz);
}


void st_reset(void)
{
  uint8_t sreg = SREG;
  cli();

  memset((void *)&st, 0, sizeof(st));
  STEP_PORT = (STEP_PORT & ~STEP_MASK) | (step_port_invert_mask & STEP_MASK);
  DIRECTION_PORT = (DIRECTION_PORT & ~DIRECTION_MASK) | (dir_port_invert_mask & DIRECTION_MASK);

  SREG = sreg;
}


//lay block chuyen dong tu planner
static uint8_t st_load_next_block(void)
{
  plan_block_t *block = plan_get_current_block();
  if (!block) return 0;

  uint8_t sreg = SREG;
  cli();
	
	//copy block sang bien st
  st.steps[X_AXIS] = block->steps[X_AXIS];
  st.steps[Y_AXIS] = block->steps[Y_AXIS];
  st.steps[Z_AXIS] = block->steps[Z_AXIS];

  st.step_event_count = block->step_event_count;
  st.step_count = block->step_event_count;

  st.counter[X_AXIS] = block->step_event_count >> 1;
  st.counter[Y_AXIS] = block->step_event_count >> 1;
  st.counter[Z_AXIS] = block->step_event_count >> 1;

  st.direction_bits = block->direction_bits;
  st.dir_outbits = block->direction_bits ^ dir_port_invert_mask;

  st.millimeters = block->millimeters;
  st.acceleration = block->acceleration;
  st.nominal_speed = block->nominal_speed;
  st.entry_speed = block->entry_speed;
  st.exit_speed = block->exit_speed;
  st.current_speed = block->entry_speed;

  if (st.current_speed < 1.0f) st.current_speed = 1.0f;
	
	//tinh sang mm
  st.mm_per_step_event = block->millimeters / (float)block->step_event_count;
  st.distance_done = 0.0f;
  st.running = 1;

	//chay timer
  OCR1A = speed_to_timer_cycles(st.current_speed);

  SREG = sreg;
  
	//xoa block khoi planner
  plan_discard_current_block();

  return 1;
}


/*
N?u stepper ðang r?nh
? l?y block ti?p theo t? planner
? b?t driver
? b?t Timer1 interrupt ð? b?t ð?u phát xung
*/
void st_prep_buffer(void)
{
  if (!st.running) {
    if (st_load_next_block()) {
      st_wake_up();
    }
  }
}


/*
1. C?u h?nh chân STEP là output
2. C?u h?nh chân DIR là output
3. C?u h?nh chân ENABLE c?a A4988 là output
4. T?t driver ban ð?u
5. C?u h?nh Timer1 ð? phát xung STEP
6. C?u h?nh Timer0 ð? h? xung STEP xu?ng LOW
7. Reset tr?ng thái stepper
*/
void stepper_init(void)
{
  st_generate_step_dir_invert_masks();

  STEP_DDR |= STEP_MASK;
  DIRECTION_DDR |= DIRECTION_MASK;
  STEPPERS_DISABLE_DDR |= (1 << STEPPERS_DISABLE_BIT);

  STEP_PORT = (STEP_PORT & ~STEP_MASK) | (step_port_invert_mask & STEP_MASK);
  DIRECTION_PORT = (DIRECTION_PORT & ~DIRECTION_MASK) | (dir_port_invert_mask & DIRECTION_MASK);

  // Disable drivers initially.
  STEPPERS_DISABLE_PORT |= (1 << STEPPERS_DISABLE_BIT);

  // Timer1 CTC mode, no prescaler.
  TCCR1A = 0;
  TCCR1B = 0;
  TCCR1B |= (1 << WGM12);
  TCCR1B |= (1 << CS10);
  OCR1A = 40000;
  TIMSK1 &= ~(1 << OCIE1A);

  // Timer0 normal mode. Used only for step pulse reset.
  TCCR0A = 0;
  TCCR0B = 0;
  TIMSK0 |= (1 << TOIE0);

  st_reset();
}


/*
noi suy va phat xung
M?i l?n Timer1 t?i th?i ði?m so sánh, hàm ng?t này ch?y m?t l?n:
1. N?u chýa có block th? l?y block m?i
2. Xu?t chi?u DIR
3. Tính tr?c nào c?n phát STEP b?ng Bresenham
4. Kéo chân STEP lên HIGH
5. C?p nh?t t?c ð?/gia t?c cho l?n interrupt sau
*/
ISR(TIMER1_COMPA_vect)
{
  if (!st.running) {
    if (!st_load_next_block()) {
      st_go_idle();
      return;
    }
  }

  DIRECTION_PORT = (DIRECTION_PORT & ~DIRECTION_MASK) | (st.dir_outbits & DIRECTION_MASK);

  uint8_t step_outbits = 0;


	//=======================noi suy Bresenham x/y/z===========================
	/*
	step_event_count = s? interrupt l?n nh?t c?a ðo?n ch?y
	steps[X]         = s? step X c?n ch?y
	steps[Y]         = s? step Y c?n ch?y
	steps[Z]         = s? step Z c?n ch?y
	vi du
	X = 1000 step
	Y = 500 step
	Z = 0 step
	=> step_event_count = 1000
	moi lam timer1 chay
	X g?n nhý phát xung m?i l?n
	Y phát xung kho?ng 2 l?n Timer1 th? 1 l?n
	Z không phát
	*/
  st.counter[X_AXIS] += st.steps[X_AXIS];
  if (st.counter[X_AXIS] > st.step_event_count) {
    step_outbits |= (1 << X_STEP_BIT);
    st.counter[X_AXIS] -= st.step_event_count;

    if (st.direction_bits & (1 << X_DIRECTION_BIT)) sys_position[X_AXIS]--;
    else sys_position[X_AXIS]++;
  }

  st.counter[Y_AXIS] += st.steps[Y_AXIS];
  if (st.counter[Y_AXIS] > st.step_event_count) {
    step_outbits |= (1 << Y_STEP_BIT);
    st.counter[Y_AXIS] -= st.step_event_count;

    if (st.direction_bits & (1 << Y_DIRECTION_BIT)) sys_position[Y_AXIS]--;
    else sys_position[Y_AXIS]++;
  }

  st.counter[Z_AXIS] += st.steps[Z_AXIS];
  if (st.counter[Z_AXIS] > st.step_event_count) {
    step_outbits |= (1 << Z_STEP_BIT);
    st.counter[Z_AXIS] -= st.step_event_count;

    if (st.direction_bits & (1 << Z_DIRECTION_BIT)) sys_position[Z_AXIS]--;
    else sys_position[Z_AXIS]++;
  }
	//==========================================================================


  STEP_PORT = (STEP_PORT & ~STEP_MASK) | ((step_outbits ^ step_port_invert_mask) & STEP_MASK);

  // Timer0 /8, overflow after about 20us.
  TCNT0 = 256 - 40;
  TCCR0B = (1 << CS01);

  if (st.step_count > 0) st.step_count--;

  st.distance_done += st.mm_per_step_event;

  // Trapezoid velocity update.
  /*
  distance_left  = c?n bao nhiêu mm n?a th? h?t ðo?n
  stop_distance  = c?n bao nhiêu mm ð? gi?m t?c v? exit_speed
  
  t?c ð? cao  ? OCR1A nh? ? interrupt nhanh hõn ? step nhanh hõn
  t?c ð? th?p ? OCR1A l?n ? interrupt ch?m hõn ? step ch?m hõn
  => tao gia toc/ giam toc cho motor
  */
  float distance_left = st.millimeters - st.distance_done;

  float stop_distance = 0.0f;
  if (st.current_speed > st.exit_speed) {
    stop_distance = (st.current_speed * st.current_speed - st.exit_speed * st.exit_speed) /
                    (2.0f * st.acceleration);
  }

  if (distance_left <= stop_distance) {
    if (st.current_speed > 1.0f) {
      st.current_speed -= st.acceleration * (st.mm_per_step_event / st.current_speed);
      if (st.current_speed < 1.0f) st.current_speed = 1.0f;
    }
  } else if (st.current_speed < st.nominal_speed) {
    st.current_speed += st.acceleration * (st.mm_per_step_event / st.current_speed);
    if (st.current_speed > st.nominal_speed) st.current_speed = st.nominal_speed;
  }

  OCR1A = speed_to_timer_cycles(st.current_speed);

  if (st.step_count == 0) {
    st.running = 0;
  }
}

ISR(TIMER0_OVF_vect)
{
  STEP_PORT = (STEP_PORT & ~STEP_MASK) | (step_port_invert_mask & STEP_MASK);
  TCCR0B = 0;
}
