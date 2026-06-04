/*
  motion_control.c - High-level gantry crane algorithm

  Algorithm:
  1. Home to cell 1, set X0 Y0.
  2. Wait photo sensor detect object.
  3. Lower Z until Z contact switch.
  4. Turn on electromagnet, lift object.
  5. Move through selected cells 2..5.
  6. Move to end cell 6.
  7. Turn fan on 5s, then off.
  8. Lower object, turn off magnet.
  9. Return home and repeat.
*/

#include "grbl.h"

/* =========================================================
 * Config
 * ========================================================= */

#define LINE_BUFFER_SIZE           96

#define CELL_COUNT                 6
#define CELL_HOME                  0
#define CELL_1                     1
#define CELL_2                     2
#define CELL_3                     3
#define CELL_4                     4
#define CELL_END                   5

#define AXIS_NONE                  255

#define DIR_NEGATIVE               -1
#define DIR_POSITIVE                1

#define MOVE_NOOP                  0
#define MOVE_OK                    1
#define MOVE_BUSY                  2
#define MOVE_LIMIT                 3
#define MOVE_ERROR                 4
#define MOVE_NOT_HOMED             5
#define MOVE_AUTO_RUNNING          6

#define HOMING_PULL_OFF_MM         5.0f
#define HOMING_STEP_DELAY_US       500
#define HOMING_STEP_BATCH          5
#define HOMING_SEARCH_X_MM         600.0f
#define HOMING_SEARCH_Y_MM         600.0f
#define HOMING_DELAY_TICKS         300       /* 300 * 10ms = 3s */

#define Z_LIMIT_SEARCH_MM          220.0f    /* safe search target beyond expected Z+ limit */
#define CELL_Z_TRAVEL_MM           120.0f    /* Z travel at middle cells */
#define END_Z_PULL_UP_MM           50.0f      /* after END Z+ limit, pull Z- up 50mm before going HOME */
#define CELL_DWELL_TICKS           300       /* 300 * 10ms = 3s */

#define FAN_RUN_TICKS              500       /* 500 * 10ms = 5s */

#define STATUS_PERIOD_LOOP         50        /* 50 * 10ms = 500ms */
#define LCD_PERIOD_LOOP            50        /* 50 * 10ms = 500ms */

#define POS_TOL_MM                 1.0f
#define Z_TOL_MM                   2.0f

/* =========================================================
 * Types
 * ========================================================= */

typedef struct {
    char name[6];
    float x;
    float y;
    float z;
    uint8_t mandatory;
    uint8_t selected;
} auto_cell_t;

typedef enum {
    MODE_NOT_HOMED = 0,
    MODE_IDLE,
    MODE_JOG,
    MODE_AUTO,
    MODE_HOMING,
    MODE_ALARM
} run_mode_t;

typedef enum {
    HOME_IDLE = 0,
    HOME_SEARCH_X,
    HOME_SEARCH_Y,
    HOME_WAIT_3S,
    HOME_PULL_X,
    HOME_PULL_Y,
    HOME_SET_ZERO
} home_phase_t;

typedef enum {
    AUTO_IDLE = 0,
    AUTO_WAIT_OBJECT,
    AUTO_PICK_Z_TO_LIMIT,
    AUTO_PICK_Z_RETURN_ZERO,
    AUTO_SENSOR_LOST_RETURN_ZERO,
    AUTO_MOVE_XY,
    AUTO_AFTER_XY,
    AUTO_CELL_Z_DOWN,
    AUTO_CELL_DWELL,
    AUTO_CELL_Z_UP,
    AUTO_AFTER_CELL_Z_UP,
    AUTO_END_Z_TO_LIMIT,
    AUTO_END_Z_PULL_UP,
    AUTO_END_Z_RETURN_ZERO,
    AUTO_END_FAN_DELAY,
    AUTO_END_MOVE_HOME,
    AUTO_END_RETURN_HOME_FAN,
    AUTO_DONE
} auto_phase_t;

/* =========================================================
 * Globals
 * ========================================================= */

static char line_buffer[LINE_BUFFER_SIZE];
static uint8_t line_index = 0;

static float manual_step_mm = 5.0f;
static float manual_feed_mm_min = 6000.0f;
static float auto_feed_xy_mm_min = 6000.0f;
static float auto_feed_z_mm_min = 4200.0f;

static volatile run_mode_t run_mode = MODE_NOT_HOMED;
static home_phase_t home_phase = HOME_IDLE;
static auto_phase_t auto_phase = AUTO_IDLE;

static uint8_t is_homed = 0;
static uint8_t auto_enabled = 0;
static uint8_t sensor_state = 0;

static uint8_t fan_state = 0;
static uint8_t magnet_state = 0;
static uint8_t magnet_flag = 0;

static uint8_t limit_alarm = 0;
static uint8_t alarm_axis = AXIS_NONE;
static int8_t alarm_dir = 0;
static char alarm_text[32] = "NONE";

static volatile uint8_t current_axis = AXIS_NONE;
static volatile int8_t current_dir = 0;

static uint8_t last_running = 0;
static uint16_t status_counter = 0;
static uint16_t lcd_counter = 0;

static uint32_t product_count = 0;

static uint32_t home_step_count = 0;
static uint32_t home_max_steps = 0;
static uint32_t home_pull_steps = 0;
static uint16_t home_delay_ticks = 0;

static uint16_t wait_ticks = 0;
static uint16_t fan_ticks = 0;
static uint8_t fan_count_done = 0;

static uint8_t auto_route[CELL_COUNT];
static uint8_t auto_route_count = 0;
static uint8_t auto_route_pos = 0;
static uint8_t auto_current_cell = CELL_HOME;
static uint8_t path_configured = 0;

/*
 * route_update_busy = 1 nghĩa là WinForms đang gửi một bộ dữ liệu quỹ đạo mới.
 * MCU sẽ không cho AUTO_WAIT_OBJECT bắt đầu chu trình cho đến khi nhận APPLYROUTE.
 *
 * Thứ tự đúng từ WinForms:
 *   ROUTE <name>
 *   CELL ...
 *   PATH ...
 *   APPLYROUTE
 */
static uint8_t route_update_busy = 0;

static char current_route_name[12] = "Default";

static float z_pick_limit_pos_mm = 0.0f;
static float z_end_limit_pos_mm = 0.0f;
//static float z_transport_pos_mm = 0.0f;

static auto_cell_t cells[CELL_COUNT] = {
    {"HOME", 0.0f,   0.0f,   0.0f, 1, 1},
    {"1",    135.0f, 0.0f,   0.0f, 0, 1},
    {"2",    270.0f, 0.0f,   0.0f, 0, 1},
    {"3",    270.0f, 270.0f, 0.0f, 0, 1},
    {"4",    135.0f, 270.0f, 0.0f, 0, 1},
    {"END",  0.0f,   270.0f, 0.0f, 1, 1}
};

/* Forward declarations */
static void report_status(void);
static void report_limits(void);
static void report_one_cell(uint8_t idx);
static void lcd_update(void);
static void auto_build_route(void);

/* Store constant UART text in FLASH to save ATmega328P SRAM. */
static void serial_write_pgm(const char *p)
{
    char c;

    while ((c = (char)pgm_read_byte(p++)) != 0) {
        serial_write((uint8_t)c);
    }
}

static void serial_write_ln_pgm(const char *p)
{
    serial_write_pgm(p);
    serial_write('\r');
    serial_write('\n');
}

#define SW(s)   serial_write_pgm(PSTR(s))
#define SWL(s)  serial_write_ln_pgm(PSTR(s))

/* =========================================================
 * Utility
 * ========================================================= */

static void write_float2(float value)
{
    char buf[18];
    dtostrf(value, 0, 2, buf);
    serial_write_string(buf);
}

static void write_u32(uint32_t value)
{
    char buf[12];
    ultoa(value, buf, 10);
    serial_write_string(buf);
}

static uint8_t starts_with(const char *s, const char *prefix)
{
    while (*prefix) {
        if (*s++ != *prefix++) {
            return 0;
        }
    }

    return 1;
}

static void uppercase_line(char *s)
{
    while (*s) {
        if (*s >= 'a' && *s <= 'z') {
            *s -= 32;
        }
        s++;
    }
}

static char *skip_spaces(char *p)
{
    while (*p == ' ') {
        p++;
    }

    return p;
}

static float parse_number_after_space(char *line, float default_value)
{
    char *p = line;

    while (*p && *p != ' ') {
        p++;
    }

    while (*p == ' ') {
        p++;
    }

    if (*p == 0) {
        return default_value;
    }

    return atof(p);
}

static uint8_t parse_word_float(char *line, char word, float *value)
{
    char *p = line;

    /*
     * Parse tokens like X135, Y0, Z0, F1000 only.
     * The letter must be at token start and must be followed by a number.
     */
    while (*p) {
        if (*p == word) {
            char prev = (p == line) ? ' ' : *(p - 1);
            char next = *(p + 1);

            if ((p == line || prev == ' ') &&
                (next == '-' || next == '+' || next == '.' || (next >= '0' && next <= '9'))) {
                *value = atof(p + 1);
                return 1;
            }
        }

        p++;
    }

    return 0;
}

/* =========================================================
 * Position / planner helpers
 * ========================================================= */

static void get_mpos(float *pos)
{
    uint8_t sreg = SREG;
    cli();

    int32_t sx = sys_position[X_AXIS];
    int32_t sy = sys_position[Y_AXIS];
    int32_t sz = sys_position[Z_AXIS];

    SREG = sreg;

    pos[X_AXIS] = (float)sx / planner_settings.steps_per_mm[X_AXIS];
    pos[Y_AXIS] = (float)sy / planner_settings.steps_per_mm[Y_AXIS];
    pos[Z_AXIS] = (float)sz / planner_settings.steps_per_mm[Z_AXIS];
}

static void sync_planner_to_current_position(void)
{
    uint8_t sreg = SREG;
    cli();

    int32_t sx = sys_position[X_AXIS];
    int32_t sy = sys_position[Y_AXIS];
    int32_t sz = sys_position[Z_AXIS];

    SREG = sreg;

    plan_set_position_steps(sx, sy, sz);
}

static float coord_round(float value)
{
    return (float)((int32_t)lroundf(value));
}

static uint8_t position_close(float x, float y, float z, float tol)
{
    float p[N_AXIS];
    get_mpos(p);

    if (fabsf(p[X_AXIS] - x) > tol) return 0;
    if (fabsf(p[Y_AXIS] - y) > tol) return 0;
    if (fabsf(p[Z_AXIS] - z) > tol) return 0;

    return 1;
}

static void stop_motion(void)
{
    st_go_idle();
    plan_reset();
    sync_planner_to_current_position();

    current_axis = AXIS_NONE;
    current_dir = 0;
}

/*
 * Sau khi AUTO chạy xong 1 chu trình, auto_enabled vẫn có thể = 1 để chờ vật.
 * Nếu người dùng Jog/GOTO thủ công thì phải hủy trạng thái chờ Auto này,
 * nếu không auto_process() sẽ kéo máy về HOME.
 */
static void cancel_auto_waiting_for_manual(void)
{
    if (auto_enabled) {
        auto_enabled = 0;
        auto_phase = AUTO_IDLE;
        auto_route_pos = 0;
        auto_route_count = 0;
        route_update_busy = 0;
    }
}

static uint8_t limit_x_active(void)
{
    return limits_x_min();
}

static uint8_t limit_y_active(void)
{
    return limits_y_min();
}

static uint8_t limit_z_plus_active(void)
{
    return limits_z();
}


static uint8_t blocked_by_limit_before_start(uint8_t axis, int8_t dir)
{
    if (axis == X_AXIS && limit_x_active()) {
        if (limit_alarm && alarm_axis == X_AXIS && dir != alarm_dir) {
            return 0;
        }
        return 1;
    }

    if (axis == Y_AXIS && limit_y_active()) {
        if (limit_alarm && alarm_axis == Y_AXIS && dir != alarm_dir) {
            return 0;
        }
        return 1;
    }

    if (axis == Z_AXIS && dir > 0 && limit_z_plus_active()) {
        return 1;
    }

    return 0;
}

static uint8_t start_motion_abs(
    float x,
    float y,
    float z,
    float feed_mm_min,
    uint8_t axis,
    int8_t dir,
    uint8_t limit_check)
{
    x = coord_round(x);
    y = coord_round(y);
    z = coord_round(z);

    if (st_is_running()) {
        return MOVE_BUSY;
    }

    if (feed_mm_min <= 0.0f) {
        return MOVE_ERROR;
    }

    if (limit_check && blocked_by_limit_before_start(axis, dir)) {
        return MOVE_LIMIT;
    }

    if (position_close(x, y, z, 0.01f)) {
        return MOVE_NOOP;
    }

    float target[N_AXIS];
    target[X_AXIS] = x;
    target[Y_AXIS] = y;
    target[Z_AXIS] = z;

    plan_line_data_t data;
    data.feed_rate = feed_mm_min;
    data.rapid_motion = 0;

    if (plan_buffer_line(target, &data) != PLAN_OK) {
        return MOVE_ERROR;
    }

    current_axis = axis;
    current_dir = dir;

    st_prep_buffer();
    st_wake_up();

    return MOVE_OK;
}

static uint8_t start_motion_auto(float x, float y, float z, float feed_mm_min)
{
    float p[N_AXIS];
    int8_t dir = 0;
    uint8_t axis = AXIS_NONE;

    get_mpos(p);

    if (fabsf(x - p[X_AXIS]) > 0.01f) {
        axis = X_AXIS;
        dir = (x > p[X_AXIS]) ? DIR_POSITIVE : DIR_NEGATIVE;
    } else if (fabsf(y - p[Y_AXIS]) > 0.01f) {
        axis = Y_AXIS;
        dir = (y > p[Y_AXIS]) ? DIR_POSITIVE : DIR_NEGATIVE;
    } else if (fabsf(z - p[Z_AXIS]) > 0.01f) {
        axis = Z_AXIS;
        dir = (z > p[Z_AXIS]) ? DIR_POSITIVE : DIR_NEGATIVE;
    }

    return start_motion_abs(x, y, z, feed_mm_min, axis, dir, 0);
}

static uint8_t start_z_to(float z_target)
{
    float p[N_AXIS];
    int8_t dir;

    get_mpos(p);

    dir = (z_target > p[Z_AXIS]) ? DIR_POSITIVE : DIR_NEGATIVE;

    return start_motion_abs(
        p[X_AXIS],
        p[Y_AXIS],
        z_target,
        auto_feed_z_mm_min,
        Z_AXIS,
        dir,
        0
    );
}


static uint8_t start_z_to_limit_search(void)
{
    float p[N_AXIS];

    get_mpos(p);

    return start_motion_abs(
        p[X_AXIS],
        p[Y_AXIS],
        p[Z_AXIS] + Z_LIMIT_SEARCH_MM,
        auto_feed_z_mm_min,
        Z_AXIS,
        DIR_POSITIVE,
        0
    );
}

static uint8_t start_jog_axis(uint8_t axis, int8_t dir)
{
    float p[N_AXIS];

    if (!is_homed) {
        return MOVE_NOT_HOMED;
    }

    if (run_mode == MODE_AUTO || run_mode == MODE_HOMING) {
        return MOVE_AUTO_RUNNING;
    }

    get_mpos(p);

    if (axis == X_AXIS) {
        p[X_AXIS] += (dir > 0) ? manual_step_mm : -manual_step_mm;
    } else if (axis == Y_AXIS) {
        p[Y_AXIS] += (dir > 0) ? manual_step_mm : -manual_step_mm;
    } else if (axis == Z_AXIS) {
        p[Z_AXIS] += (dir > 0) ? manual_step_mm : -manual_step_mm;
    }

    /*
     * Jog thủ công hủy chế độ AUTO đang chờ vật.
     */
    cancel_auto_waiting_for_manual();

    run_mode = MODE_JOG;

    /*
     * Unified speed setting:
     * - X/Y jog use XY speed.
     * - Z jog uses Z speed.
     */
    return start_motion_abs(
        p[X_AXIS],
        p[Y_AXIS],
        p[Z_AXIS],
        (axis == Z_AXIS) ? auto_feed_z_mm_min : manual_feed_mm_min,
        axis,
        dir,
        1
    );
}

/* =========================================================
 * Relay helpers
 * ========================================================= */

static void fan_on(void)
{
    relay_fan_on();
    fan_state = 1;
}

static void fan_off(void)
{
    relay_fan_off();
    fan_state = 0;
}

static void magnet_on(void)
{
    relay_magnet_on();
    magnet_state = 1;
    magnet_flag = 1;
}

static void magnet_off(void)
{
    relay_magnet_off();
    magnet_state = 0;
    magnet_flag = 0;
}

static void outputs_init_off(void)
{
    relay_all_off();
    fan_state = 0;
    magnet_state = 0;
    magnet_flag = 0;
}

/* =========================================================
 * LCD
 * ========================================================= */

static const char *mode_text(void)
{
    if (run_mode == MODE_HOMING) return "Home";
    if (run_mode == MODE_AUTO) return "Auto";
    if (run_mode == MODE_JOG || st_is_running()) return "Jog";
    if (run_mode == MODE_ALARM || limit_alarm) return "Alarm";
    if (!is_homed) return "NoHome";
    return "Idle";
}

static void lcd_write_char(char c)
{
    char s[2];
    s[0] = c;
    s[1] = 0;
    lcd_print(s);
}

static void lcd_write_padded(const char *text)
{
    uint8_t i = 0;

    while (text[i] && i < 16) {
        lcd_write_char(text[i]);
        i++;
    }

    while (i < 16) {
        lcd_write_char(' ');
        i++;
    }
}

static void lcd_update(void)
{
    char line[17];
    char count_buf[10];
    uint8_t pos = 0;
    const char *m;
    const char *cell;
    float p[N_AXIS];
    int16_t xi;
    int16_t yi;
    int16_t zi;
    char tmp[8];

    get_mpos(p);

    lcd_set_cursor(0, 0);
    memset(line, ' ', 16);
    line[16] = 0;

    /* LCD line 1: state + route name + product count. */
    m = mode_text();
    while (*m && pos < 16) line[pos++] = *m++;

    if (pos < 16) line[pos++] = ' ';

    for (uint8_t i = 0; current_route_name[i] && pos < 16; i++) {
        line[pos++] = current_route_name[i];
    }

    if (pos < 16) line[pos++] = ' ';
    if (pos < 16) line[pos++] = 'C';
    if (pos < 16) line[pos++] = ':';

    ultoa(product_count, count_buf, 10);
    for (uint8_t i = 0; count_buf[i] && pos < 16; i++) {
        line[pos++] = count_buf[i];
    }

    lcd_write_padded(line);

    lcd_set_cursor(0, 1);
    memset(line, ' ', 16);
    line[16] = 0;

    pos = 0;
    cell = cells[auto_current_cell].name;
    if (pos < 16) line[pos++] = 'P';
    if (pos < 16) line[pos++] = ':';
    while (*cell && pos < 16) line[pos++] = *cell++;
    if (pos < 16) line[pos++] = ' ';

    xi = (int16_t)lroundf(p[X_AXIS]);
    yi = (int16_t)lroundf(p[Y_AXIS]);
    zi = (int16_t)lroundf(p[Z_AXIS]);

    if (pos < 16) line[pos++] = 'X';
    itoa(xi, tmp, 10);
    for (uint8_t i = 0; tmp[i] && pos < 16; i++) line[pos++] = tmp[i];

    if (pos < 16) line[pos++] = ' ';
    if (pos < 16) line[pos++] = 'Y';
    itoa(yi, tmp, 10);
    for (uint8_t i = 0; tmp[i] && pos < 16; i++) line[pos++] = tmp[i];

    if (pos < 16) line[pos++] = ' ';
    if (pos < 16) line[pos++] = 'Z';
    itoa(zi, tmp, 10);
    for (uint8_t i = 0; tmp[i] && pos < 16; i++) line[pos++] = tmp[i];

    lcd_write_padded(line);
}

/* =========================================================
 * Report
 * ========================================================= */

static void report_limits(void)
{
    SW("LIMITS:");
    SW("X=");
    serial_write(limit_x_active() ? '1' : '0');
    SW(",Y=");
    serial_write(limit_y_active() ? '1' : '0');
    SW(",ZP=");
    serial_write(limit_z_plus_active() ? '1' : '0');
    SWL("");
}

static void report_status(void)
{
    float p[N_AXIS];

    get_mpos(p);

    SW("<");
    serial_write_string(mode_text());

    SW("|MPos:");
    write_float2(p[X_AXIS]);
    serial_write(',');
    write_float2(p[Y_AXIS]);
    serial_write(',');
    write_float2(p[Z_AXIS]);

    SW("|Step:");
    write_float2(manual_step_mm);

    SW("|Feed:");
    write_float2(manual_feed_mm_min);

    SW("|AutoFeedXY:");
    write_float2(auto_feed_xy_mm_min);

    SW("|AutoFeedZ:");
    write_float2(auto_feed_z_mm_min);

    SW("|Auto:");
    serial_write(run_mode == MODE_AUTO ? '1' : '0');

    SW("|Homed:");
    serial_write(is_homed ? '1' : '0');

    SW("|Sensor:");
    serial_write(sensor_state ? '1' : '0');

    SW("|Object:");
    serial_write(sensor_state ? '1' : '0');

    SW("|X:");
    serial_write(limit_x_active() ? '1' : '0');

    SW("|Y:");
    serial_write(limit_y_active() ? '1' : '0');

    SW("|ZP:");
    serial_write(limit_z_plus_active() ? '1' : '0');

    SW("|Fan:");
    serial_write(fan_state ? '1' : '0');

    SW("|Magnet:");
    serial_write(magnet_state ? '1' : '0');

    SW("|MagFlag:");
    serial_write(magnet_flag ? '1' : '0');

    SW("|Count:");
    write_u32(product_count);

    SW("|Cell:");
    serial_write_string(cells[auto_current_cell].name);

    SW("|Route:");
    serial_write_string(current_route_name);

    SW("|Alarm:");
    serial_write_string(alarm_text);

    SWL(">");
}

static void report_one_cell(uint8_t idx)
{
    SW("CELL:");
    serial_write_string(cells[idx].name);
    serial_write(',');
    write_float2(cells[idx].x);
    serial_write(',');
    write_float2(cells[idx].y);
    serial_write(',');
    write_float2(cells[idx].z);
    serial_write(',');
    serial_write(cells[idx].selected ? '1' : '0');
    serial_write(',');
    serial_write(cells[idx].mandatory ? '1' : '0');
    SWL("");
}

static void report_all_cells(void)
{
    for (uint8_t i = 0; i < CELL_COUNT; i++) {
        report_one_cell(i);
    }

    SWL("cells:done");
}

/* =========================================================
 * Cell / path commands
 * ========================================================= */

static uint8_t cell_index_from_token(char *p, uint8_t *idx)
{
    int n;

    p = skip_spaces(p);

    if (starts_with(p, "HOME") || starts_with(p, "H")) {
        *idx = CELL_HOME;
        return 1;
    }

    if (starts_with(p, "END") || starts_with(p, "E")) {
        *idx = CELL_END;
        return 1;
    }

    n = atoi(p);

    if (n == 1) { *idx = CELL_1; return 1; }
    if (n == 2) { *idx = CELL_2; return 1; }
    if (n == 3) { *idx = CELL_3; return 1; }
    if (n == 4) { *idx = CELL_4; return 1; }

    return 0;
}

static void command_set_cell(char *line)
{
    uint8_t idx;
    char *p;
    float x;
    float y;
    float z;

    if (run_mode == MODE_AUTO || run_mode == MODE_HOMING || st_is_running()) {
        SWL("error:busy");
        return;
    }

    p = line + 4;

    if (!cell_index_from_token(p, &idx)) {
        SWL("error:bad_cell");
        return;
    }

    x = cells[idx].x;
    y = cells[idx].y;
    z = cells[idx].z;

    parse_word_float(line, 'X', &x);
    parse_word_float(line, 'Y', &y);
    parse_word_float(line, 'Z', &z);

    cells[idx].x = coord_round(x);
    cells[idx].y = coord_round(y);
    cells[idx].z = coord_round(z);

    SWL("cell:ok");
    report_one_cell(idx);
}

static void command_set_path(char *line)
{
    uint8_t idx;
    char *p;

    if (run_mode == MODE_AUTO || run_mode == MODE_HOMING || st_is_running()) {
        SWL("error:busy");
        return;
    }

    p = line + 4;

    if (!cell_index_from_token(p, &idx)) {
        SWL("error:bad_cell");
        return;
    }

    if (cells[idx].mandatory) {
        cells[idx].selected = 1;
        SWL("path:mandatory");
        report_one_cell(idx);
        return;
    }

    p = skip_spaces(p);
    while (*p && *p != ' ') p++;
    p = skip_spaces(p);

    if (starts_with(p, "ON") || starts_with(p, "1")) {
        cells[idx].selected = 1;
    } else if (starts_with(p, "OFF") || starts_with(p, "0")) {
        cells[idx].selected = 0;
    } else {
        SWL("error:bad_path_value");
        return;
    }

    path_configured = 1;

    /*
     * Nếu không ở trong phiên cập nhật ROUTE/CELL/PATH thì cập nhật route ngay.
     * Trường hợp WinForms mới sẽ đợi APPLYROUTE để build một lần sau cùng.
     */
    if (!route_update_busy && auto_enabled && auto_phase == AUTO_WAIT_OBJECT && run_mode == MODE_IDLE) {
        auto_build_route();
    }

    SWL("path:ok");
    report_one_cell(idx);
}

static void command_set_route(char *line)
{
    char *p = line + 5;
    uint8_t i = 0;

    /*
     * Bắt đầu nhận quỹ đạo mới từ WinForms.
     * Khóa AUTO_WAIT_OBJECT đến khi nhận APPLYROUTE.
     */
    route_update_busy = 1;

    p = skip_spaces(p);

    if (*p == 0) {
        strncpy(current_route_name, "Default", sizeof(current_route_name) - 1);
        current_route_name[sizeof(current_route_name) - 1] = 0;
        SWL("route:ok");
        report_status();
        return;
    }

    /* Save short ASCII route name for LCD/status. */
    while (*p && *p != ' ' && i < sizeof(current_route_name) - 1) {
        current_route_name[i++] = *p++;
    }

    current_route_name[i] = 0;

    SW("route:ok:");
    serial_write_ln(current_route_name);
    report_status();
}

static void command_apply_route(void)
{
    if (run_mode == MODE_AUTO || run_mode == MODE_HOMING || st_is_running()) {
        SWL("error:busy");
        return;
    }

    /*
     * Đã nhận xong ROUTE/CELL/PATH.
     * Build lại auto_route ngay tại đây để chu trình tiếp theo dùng đúng dữ liệu mới.
     */
    path_configured = 1;
    auto_build_route();
    auto_route_pos = 0;

    if (auto_phase == AUTO_WAIT_OBJECT) {
        auto_current_cell = CELL_HOME;
    }

    route_update_busy = 0;

    SWL("route:applied");
    report_all_cells();
    report_status();
}

static void auto_build_route(void)
{
    /*
     * Default route:
     * If user has not configured path, or selected no middle cell,
     * run all cells 1 -> 2 -> 3 -> 4 -> END.
     */
    auto_route_count = 0;

    if (!path_configured) {
        auto_route[auto_route_count++] = CELL_1;
        auto_route[auto_route_count++] = CELL_2;
        auto_route[auto_route_count++] = CELL_3;
        auto_route[auto_route_count++] = CELL_4;
    } else {
        for (uint8_t i = CELL_1; i <= CELL_4; i++) {
            if (cells[i].selected) {
                auto_route[auto_route_count++] = i;
            }
        }

        if (auto_route_count == 0) {
            auto_route[auto_route_count++] = CELL_1;
            auto_route[auto_route_count++] = CELL_2;
            auto_route[auto_route_count++] = CELL_3;
            auto_route[auto_route_count++] = CELL_4;
        }
    }

    auto_route[auto_route_count++] = CELL_END;
}

/* =========================================================
 * Alarm / limit
 * ========================================================= */

static const char *axis_dir_name(uint8_t axis, int8_t dir)
{
    if (axis == X_AXIS) {
        return (dir > 0) ? "LIMIT_X_PLUS" : "LIMIT_X_MINUS";
    }

    if (axis == Y_AXIS) {
        return (dir > 0) ? "LIMIT_Y_PLUS" : "LIMIT_Y_MINUS";
    }

    if (axis == Z_AXIS) {
        return (dir > 0) ? "LIMIT_Z_PLUS" : "LIMIT_Z_MINUS";
    }

    return "LIMIT_UNKNOWN";
}

static void set_alarm(uint8_t axis, int8_t dir)
{
    limit_alarm = 1;
    alarm_axis = axis;
    alarm_dir = dir;

    strncpy(alarm_text, axis_dir_name(axis, dir), sizeof(alarm_text) - 1);
    alarm_text[sizeof(alarm_text) - 1] = 0;

    run_mode = MODE_ALARM;

    SW("ALARM:");
    serial_write_ln(alarm_text);
    report_limits();
    report_status();
}

static void clear_alarm(void)
{
    limit_alarm = 0;
    alarm_axis = AXIS_NONE;
    alarm_dir = 0;
    strcpy(alarm_text, "NONE");

    limits_clear_hard_alarm();

    if (!is_homed) {
        run_mode = MODE_NOT_HOMED;
    } else {
        run_mode = MODE_IDLE;
    }

    SWL("clear:ok");
    report_status();
}

static void check_limit_while_running(void)
{
    if (!st_is_running()) {
        return;
    }

    if (current_axis == AXIS_NONE) {
        return;
    }

    if (current_axis == X_AXIS && limit_x_active()) {
        /*
         * X limit is always a safety alarm outside homing.
         * Returning HOME must go to coordinate X=0, not to the limit switch.
         */
        stop_motion();
        auto_enabled = 0;
        auto_phase = AUTO_IDLE;
        set_alarm(X_AXIS, current_dir);
        return;
    }

    if (current_axis == Y_AXIS && limit_y_active()) {
        /*
         * Y limit is always a safety alarm outside homing.
         * Returning HOME must go to coordinate Y=0, not to the limit switch.
         */
        stop_motion();
        auto_enabled = 0;
        auto_phase = AUTO_IDLE;
        set_alarm(Y_AXIS, current_dir);
        return;
    }

    /*
     * Z+ limit is handled by AUTO_PICK_Z_TO_LIMIT and AUTO_END_Z_TO_LIMIT.
     * In manual jog, Z+ limit stops Z+ motion and reports warning.
     */
    if (current_axis == Z_AXIS && current_dir > 0 && limit_z_plus_active()) {
        /* In AUTO, Z+ limit is a normal process/contact signal, not an alarm. */
        if (run_mode != MODE_AUTO) {
            stop_motion();
            set_alarm(Z_AXIS, current_dir);
        }
    }
}

/* =========================================================
 * Stop
 * ========================================================= */

static void stop_all(void)
{
    stop_motion();

    fan_off();

    if (!magnet_flag) {
        magnet_off();
    }

    auto_enabled = 0;
    auto_phase = AUTO_IDLE;
    home_phase = HOME_IDLE;
    fan_ticks = 0;
    fan_count_done = 0;

    if (is_homed) {
        run_mode = MODE_IDLE;
    } else {
        run_mode = MODE_NOT_HOMED;
    }

    SWL("stop:ok");
    report_status();
}

/* =========================================================
 * Homing
 * ========================================================= */

static void home_step_pulse(uint8_t step_bit, uint8_t dir_bit, uint8_t negative_dir)
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

static void home_start(void)
{
    if (st_is_running()) {
        SWL("error:busy");
        return;
    }

    stop_motion();
    fan_off();

    if (!magnet_flag) {
        magnet_off();
    }

    limits_disable();

    is_homed = 0;
    auto_enabled = 0;
    run_mode = MODE_HOMING;
    home_phase = HOME_SEARCH_X;

    home_step_count = 0;
    home_max_steps = (uint32_t)(HOMING_SEARCH_X_MM * planner_settings.steps_per_mm[X_AXIS]);
    home_pull_steps = 0;
    home_delay_ticks = 0;

    strcpy(alarm_text, "NONE");
    limit_alarm = 0;

    STEPPERS_DISABLE_PORT &= ~(1 << STEPPERS_DISABLE_BIT);

    SWL("home:start");
    SWL("home:x_search");
    report_status();
}

static void home_fail(const char *msg)
{
    stop_motion();

    home_phase = HOME_IDLE;
    run_mode = MODE_NOT_HOMED;
    is_homed = 0;

    limits_enable();

    strncpy(alarm_text, msg, sizeof(alarm_text) - 1);
    alarm_text[sizeof(alarm_text) - 1] = 0;

    SW("home:fail:");
    serial_write_ln(msg);
    report_status();
}

static void home_done(void)
{
    st_set_position_mm(0.0f, 0.0f, 0.0f);
    sync_planner_to_current_position();

    home_phase = HOME_IDLE;
    run_mode = MODE_IDLE;
    is_homed = 1;
    auto_current_cell = CELL_HOME;

    /* Keep PCINT disabled. main.c polls limits, so Z+ can be used as process signal. */
    limits_disable();

    SWL("home:ok");
    report_status();
}

static void home_process(void)
{
    if (run_mode != MODE_HOMING) {
        return;
    }

    switch (home_phase) {
        case HOME_SEARCH_X:
            for (uint8_t i = 0; i < HOMING_STEP_BATCH; i++) {
                if (limit_x_active()) {
                    SWL("home:x_hit");
                    home_phase = HOME_SEARCH_Y;
                    home_step_count = 0;
                    home_max_steps = (uint32_t)(HOMING_SEARCH_Y_MM * planner_settings.steps_per_mm[Y_AXIS]);
                    SWL("home:y_search");
                    return;
                }

                if (home_step_count >= home_max_steps) {
                    home_fail("x_timeout");
                    return;
                }

                home_step_pulse(X_STEP_BIT, X_DIRECTION_BIT, HOMING_X_DIR_NEGATIVE);
                home_step_count++;
            }
            return;

        case HOME_SEARCH_Y:
            for (uint8_t i = 0; i < HOMING_STEP_BATCH; i++) {
                if (limit_y_active()) {
                    SWL("home:y_hit");
                    home_phase = HOME_WAIT_3S;
                    home_delay_ticks = HOMING_DELAY_TICKS;
                    SWL("home:delay_3s");
                    return;
                }

                if (home_step_count >= home_max_steps) {
                    home_fail("y_timeout");
                    return;
                }

                home_step_pulse(Y_STEP_BIT, Y_DIRECTION_BIT, HOMING_Y_DIR_NEGATIVE);
                home_step_count++;
            }
            return;

        case HOME_WAIT_3S:
            if (home_delay_ticks > 0) {
                home_delay_ticks--;
                _delay_ms(10);
                return;
            }

            SWL("home:pull_x_plus");
            home_phase = HOME_PULL_X;
            home_step_count = 0;
            home_pull_steps = (uint32_t)(HOMING_PULL_OFF_MM * planner_settings.steps_per_mm[X_AXIS]);
            return;

        case HOME_PULL_X:
            for (uint8_t i = 0; i < HOMING_STEP_BATCH; i++) {
                if (home_step_count >= home_pull_steps) {
                    SWL("home:pull_y_plus");
                    home_phase = HOME_PULL_Y;
                    home_step_count = 0;
                    home_pull_steps = (uint32_t)(HOMING_PULL_OFF_MM * planner_settings.steps_per_mm[Y_AXIS]);
                    return;
                }

                home_step_pulse(X_STEP_BIT, X_DIRECTION_BIT, !HOMING_X_DIR_NEGATIVE);
                home_step_count++;
            }
            return;

        case HOME_PULL_Y:
            for (uint8_t i = 0; i < HOMING_STEP_BATCH; i++) {
                if (home_step_count >= home_pull_steps) {
                    home_phase = HOME_SET_ZERO;
                    return;
                }

                home_step_pulse(Y_STEP_BIT, Y_DIRECTION_BIT, !HOMING_Y_DIR_NEGATIVE);
                home_step_count++;
            }
            return;

        case HOME_SET_ZERO:
            home_done();
            return;

        default:
            home_fail("bad_home_state");
            return;
    }
}

/* =========================================================
 * AUTO cycle
 * ========================================================= */

static void auto_fail(const char *msg)
{
    stop_motion();
    fan_off();

    if (!magnet_flag) {
        magnet_off();
    }

    auto_enabled = 0;
    auto_phase = AUTO_IDLE;
    run_mode = MODE_IDLE;

    strncpy(alarm_text, msg, sizeof(alarm_text) - 1);
    alarm_text[sizeof(alarm_text) - 1] = 0;

    SW("auto:fail:");
    serial_write_ln(alarm_text);
    report_status();
}

static void auto_start(void)
{
    if (!is_homed) {
        SWL("error:not_homed");
        report_status();
        return;
    }

    if (st_is_running() || run_mode == MODE_HOMING) {
        SWL("error:busy");
        return;
    }

    /*
     * START AUTO chỉ chạy với dữ liệu hiện có trong MCU.
     * Việc cập nhật tọa độ/quỹ đạo đã do nút APPLY QUỸ ĐẠO xử lý trước đó.
     */
    auto_build_route();
    route_update_busy = 0;

    auto_enabled = 1;
    auto_phase = AUTO_WAIT_OBJECT;
    auto_route_pos = 0;
    auto_current_cell = CELL_HOME;

    /* Armed AUTO, but still Idle at HOME waiting for object. */
    run_mode = MODE_IDLE;

    SWL("auto:start");
    SWL("auto:wait_object");
    report_status();
}

static void auto_finish_cycle(void)
{
    auto_route_pos = 0;
    auto_current_cell = CELL_HOME;
    auto_phase = AUTO_WAIT_OBJECT;

    /* Cycle done at HOME. Keep AUTO armed, but report Idle. */
    run_mode = MODE_IDLE;

    SWL("cycle:done");
    SWL("auto:wait_object");
    report_status();
}

static void auto_start_or_advance(uint8_t r, auto_phase_t next_phase)
{
    if (r == MOVE_OK || r == MOVE_NOOP) {
        auto_phase = next_phase;
    } else {
        auto_fail("move_error");
    }
}

static void auto_handle_pick_z_limit(void)
{
    uint8_t r;
    float p[N_AXIS];

    /*
     * HOME pickup:
     * Z+ limit is PROCESS signal, not alarm.
     * It can be Z149, Z150, etc. Accept any reached coordinate.
     */
    stop_motion();

    get_mpos(p);
    z_pick_limit_pos_mm = coord_round(p[Z_AXIS]);

    if (!magnet_flag) {
        magnet_on();
        SWL("magnet:on");
    }

    SW("zpick:limit_at:");
    write_float2(z_pick_limit_pos_mm);
    serial_write_ln("");

    /*
     * After picking, return Z to HOME Z = 0.
     */
    SWL("auto:pick_z_return_zero");
    r = start_z_to(cells[CELL_HOME].z);
    auto_start_or_advance(r, AUTO_PICK_Z_RETURN_ZERO);

    report_status();
}

static void auto_handle_sensor_lost_during_pick(void)
{
    uint8_t r;

    /*
     * Safety:
     * At HOME, sensor starts the pickup.
     * If sensor becomes OFF before Z+ reaches limit,
     * cancel pickup and return Z to HOME Z=0.
     */
    stop_motion();

    SWL("object:lost_before_z_limit");
    SWL("auto:z_return_zero_wait_object");

    r = start_z_to(cells[CELL_HOME].z);
    auto_start_or_advance(r, AUTO_SENSOR_LOST_RETURN_ZERO);

    report_status();
}

static void auto_handle_end_z_limit(void)
{
    uint8_t r;
    float p[N_AXIS];
    float pull_target;

    /*
     * END release:
     * Z+ limit is a process signal, not an alarm.
     * After touching Z+ limit, pull Z- up about 5mm first,
     * then go HOME. This clears the Z limit before XY return.
     */
    stop_motion();

    get_mpos(p);
    z_end_limit_pos_mm = coord_round(p[Z_AXIS]);

    if (magnet_flag) {
        magnet_off();
        SWL("magnet:off");
    }

    SW("zend:limit_at:");
    write_float2(z_end_limit_pos_mm);
    serial_write_ln("");

    fan_on();
    fan_ticks = FAN_RUN_TICKS;
    fan_count_done = 0;
    SWL("fan:on");

    pull_target = z_end_limit_pos_mm - END_Z_PULL_UP_MM;

    if (pull_target < cells[CELL_HOME].z) {
        pull_target = cells[CELL_HOME].z;
    }

    SWL("auto:end_z_pull_up_5");
    r = start_z_to(pull_target);
    auto_start_or_advance(r, AUTO_END_Z_PULL_UP);

    report_status();
}


static void auto_tick_end_fan(void)
{
    /*
     * Fan runs independently during END pull-up and HOME return.
     * Count product once when fan finishes 5 seconds.
     */
    if (fan_ticks > 0) {
        fan_ticks--;

        if (fan_ticks == 0) {
            fan_off();
            SWL("fan:off");

            if (!fan_count_done) {
                product_count++;
                fan_count_done = 1;
                SWL("count:inc");
            }
        }
    }
}


//ham xu ly chay auto
static void auto_process(void)
{
    uint8_t idx;
    uint8_t r;

    if (!auto_enabled) {
        return;
    }

    /* Allow sensor monitoring while armed at HOME and State = Idle. */
    if (run_mode != MODE_AUTO) {
        if (!(run_mode == MODE_IDLE && auto_phase == AUTO_WAIT_OBJECT)) {
            return;
        }
    }

    if (auto_phase == AUTO_END_Z_PULL_UP ||
        auto_phase == AUTO_END_MOVE_HOME ||
        auto_phase == AUTO_END_FAN_DELAY) {
        auto_tick_end_fan();
    }

    /*
     * While a planner move is running, only Z+ process limit is special.
     * X/Y limits are handled elsewhere as safety limits.
     */
    if (st_is_running()) {
        if (auto_phase == AUTO_PICK_Z_TO_LIMIT && limit_z_plus_active()) {
            auto_handle_pick_z_limit();
            return;
        }

        if (auto_phase == AUTO_PICK_Z_TO_LIMIT && !sensor_state) {
            auto_handle_sensor_lost_during_pick();
            return;
        }

        if (auto_phase == AUTO_END_Z_TO_LIMIT && limit_z_plus_active()) {
            auto_handle_end_z_limit();
            return;
        }

        return;
    }

    switch (auto_phase) {
        case AUTO_WAIT_OBJECT:
            /*
             * AUTO only starts one cycle when crane is idle at HOME and sensor sees object.
             */

            /*
             * Đang nhận quỹ đạo mới thì không cho bắt đầu chu trình.
             * Đợi APPLYROUTE để đảm bảo CELL/PATH đã cập nhật xong.
             */
            if (route_update_busy) {
                return;
            }

            auto_current_cell = CELL_HOME;

            if (!position_close(cells[CELL_HOME].x, cells[CELL_HOME].y, cells[CELL_HOME].z, POS_TOL_MM)) {
                SWL("auto:move_home_before_wait");
                r = start_motion_auto(
                    cells[CELL_HOME].x,
                    cells[CELL_HOME].y,
                    cells[CELL_HOME].z,
                    auto_feed_xy_mm_min
                );
                auto_start_or_advance(r, AUTO_WAIT_OBJECT);
                return;
            }

            if (sensor_state) {
                /* Use latest CELL/PATH sent by WinForms while Idle waiting. */
                auto_build_route();
                run_mode = MODE_AUTO;

                SWL("object:detected");
                SWL("auto:pick_z_to_limit");

                r = start_z_to_limit_search();
                auto_start_or_advance(r, AUTO_PICK_Z_TO_LIMIT);
            }
            return;

        case AUTO_PICK_Z_TO_LIMIT:
            /*
             * If limit ISR or hardware stopped motor before we saw st_is_running(),
             * still accept Z+ limit as successful pickup.
             */
            if (limit_z_plus_active()) {
                auto_handle_pick_z_limit();
                return;
            }

            auto_fail("pick_z_limit_not_found");
            return;

        case AUTO_PICK_Z_RETURN_ZERO:
            /*
             * After HOME pickup, Z must be back to HOME Z = 0.
             */
            if (!position_close(cells[CELL_HOME].x, cells[CELL_HOME].y, cells[CELL_HOME].z, Z_TOL_MM)) {
                auto_fail("pick_z_not_zero");
                return;
            }

            auto_route_pos = 0;
            auto_phase = AUTO_MOVE_XY;
            return;

        case AUTO_SENSOR_LOST_RETURN_ZERO:
            /*
             * Object disappeared before Z+ reached limit.
             * Once Z returns to 0, wait for the next sensor detection.
             */
            if (!position_close(cells[CELL_HOME].x, cells[CELL_HOME].y, cells[CELL_HOME].z, Z_TOL_MM)) {
                auto_fail("sensor_lost_z_not_zero");
                return;
            }

            auto_phase = AUTO_WAIT_OBJECT;
            SWL("auto:wait_object");
            report_status();
            return;

        case AUTO_MOVE_XY:
            if (auto_route_pos >= auto_route_count) {
                auto_phase = AUTO_DONE;
                return;
            }

            idx = auto_route[auto_route_pos];
            auto_current_cell = idx;

            SW("auto:move:");
            serial_write_ln(cells[idx].name);

            /*
             * Move between cells by X/Y while Z stays at 0.
             */
            r = start_motion_auto(
                cells[idx].x,
                cells[idx].y,
                cells[CELL_HOME].z,
                auto_feed_xy_mm_min
            );

            auto_start_or_advance(r, AUTO_AFTER_XY);
            return;

        case AUTO_AFTER_XY:
            idx = auto_route[auto_route_pos];

            if (idx == CELL_END) {
                SWL("auto:end_z_to_limit");
                r = start_z_to_limit_search();
                auto_start_or_advance(r, AUTO_END_Z_TO_LIMIT);
                return;
            }

            /*
             * Middle cells only:
             * Z+ to 130, delay 3s, then Z- to 0.
             */
            SW("auto:cell_z_plus_130:");
            serial_write_ln(cells[idx].name);

            r = start_z_to(CELL_Z_TRAVEL_MM);
            auto_start_or_advance(r, AUTO_CELL_Z_DOWN);
            return;

        case AUTO_CELL_Z_DOWN:
            wait_ticks = CELL_DWELL_TICKS;
            auto_phase = AUTO_CELL_DWELL;
            SWL("auto:cell_delay_3s");
            return;

        case AUTO_CELL_DWELL:
            if (wait_ticks > 0) {
                wait_ticks--;
                return;
            }

            idx = auto_route[auto_route_pos];

            SW("auto:cell_z_return_zero:");
            serial_write_ln(cells[idx].name);

            r = start_z_to(cells[CELL_HOME].z);
            auto_start_or_advance(r, AUTO_CELL_Z_UP);
            return;

        case AUTO_CELL_Z_UP:
            auto_phase = AUTO_AFTER_CELL_Z_UP;
            return;

        case AUTO_AFTER_CELL_Z_UP:
            auto_route_pos++;
            auto_phase = AUTO_MOVE_XY;
            return;

        case AUTO_END_Z_TO_LIMIT:
            if (limit_z_plus_active()) {
                auto_handle_end_z_limit();
                return;
            }

            auto_fail("end_z_limit_not_found");
            return;

        case AUTO_END_Z_PULL_UP:
            /*
             * Z has pulled off the END limit by about 5mm.
             * Now goto HOME (X0 Y0 Z0).
             */
            SWL("auto:move_home");
            r = start_motion_auto(
                cells[CELL_HOME].x,
                cells[CELL_HOME].y,
                cells[CELL_HOME].z,
                auto_feed_xy_mm_min
            );
            auto_start_or_advance(r, AUTO_END_MOVE_HOME);
            return;

        case AUTO_END_Z_RETURN_ZERO:
            /*
             * Old phase kept for compatibility.
             */
            auto_phase = AUTO_END_Z_PULL_UP;
            return;

        case AUTO_END_FAN_DELAY:
            /*
             * If HOME is reached before fan finishes, wait here until fan ends.
             */
            if (fan_ticks == 0) {
                auto_finish_cycle();
            }
            return;

        case AUTO_END_MOVE_HOME:
            /*
             * Finished returning to HOME.
             * Must reach coordinate HOME = X0 Y0 Z0.
             * If X/Y limit is touched before reaching this coordinate,
             * check_limit_while_running() will alarm.
             */
            if (!position_close(cells[CELL_HOME].x, cells[CELL_HOME].y, cells[CELL_HOME].z, POS_TOL_MM)) {
                auto_fail("home_return_error");
                return;
            }

            if (fan_ticks > 0) {
                auto_phase = AUTO_END_FAN_DELAY;
                return;
            }

            auto_finish_cycle();
            return;

        case AUTO_END_RETURN_HOME_FAN:
            /*
             * Old phase kept for compatibility. New code uses:
             * END_Z_RETURN_ZERO -> END_FAN_DELAY -> END_MOVE_HOME.
             */
            auto_phase = AUTO_END_Z_PULL_UP;
            return;

        case AUTO_DONE:
            auto_finish_cycle();
            return;

        default:
            auto_fail("bad_auto_state");
            return;
    }
}


/* =========================================================
 * Commands
 * ========================================================= */

static void jog_result(uint8_t r)
{
    if (r == MOVE_OK) {
        SWL("ok");
    } else if (r == MOVE_NOOP) {
        SWL("ok:no_motion");
    } else if (r == MOVE_BUSY) {
        SWL("error:busy");
    } else if (r == MOVE_LIMIT) {
        SWL("error:limit");
    } else if (r == MOVE_NOT_HOMED) {
        SWL("error:not_homed");
    } else if (r == MOVE_AUTO_RUNNING) {
        SWL("error:auto_running");
    } else {
        SWL("error:move");
    }

    report_limits();
    report_status();
}

static void command_goto(char *line)
{
    float p[N_AXIS];
    float x;
    float y;
    float z;
    float f;
    uint8_t r;

    if (run_mode == MODE_AUTO || run_mode == MODE_HOMING) {
        SWL("error:auto_running");
        return;
    }

    if (!is_homed) {
        SWL("error:not_homed");
        return;
    }

    get_mpos(p);

    x = p[X_AXIS];
    y = p[Y_AXIS];
    z = p[Z_AXIS];
    f = manual_feed_mm_min;

    parse_word_float(line, 'X', &x);
    parse_word_float(line, 'Y', &y);
    parse_word_float(line, 'Z', &z);
    parse_word_float(line, 'F', &f);

    /*
     * GOTO thủ công hủy chế độ AUTO đang chờ vật.
     */
    cancel_auto_waiting_for_manual();

    run_mode = MODE_JOG;
    r = start_motion_abs(x, y, z, f, AXIS_NONE, 0, 1);

    if (r != MOVE_OK && r != MOVE_NOOP) {
        run_mode = MODE_IDLE;
    }

    jog_result(r);
}

static void command_jogi(char *line)
{
    float p[N_AXIS];
    float dx = 0.0f;
    float dy = 0.0f;
    float dz = 0.0f;
    float f = manual_feed_mm_min;
    uint8_t r;

    if (run_mode == MODE_AUTO || run_mode == MODE_HOMING) {
        SWL("error:auto_running");
        return;
    }

    if (!is_homed) {
        SWL("error:not_homed");
        return;
    }

    get_mpos(p);

    parse_word_float(line, 'X', &dx);
    parse_word_float(line, 'Y', &dy);
    parse_word_float(line, 'Z', &dz);
    parse_word_float(line, 'F', &f);

    /*
     * JOGI thủ công hủy chế độ AUTO đang chờ vật.
     */
    cancel_auto_waiting_for_manual();

    run_mode = MODE_JOG;
    r = start_motion_abs(
        p[X_AXIS] + dx,
        p[Y_AXIS] + dy,
        p[Z_AXIS] + dz,
        f,
        AXIS_NONE,
        0,
        1
    );

    if (r != MOVE_OK && r != MOVE_NOOP) {
        run_mode = MODE_IDLE;
    }

    jog_result(r);
}

//ham xu ly cmd nhan tu winform
static void process_command(char *line)
{
    uint8_t r;

    uppercase_line(line);

    if (line[0] == 0) {
        return;
    }

    if (starts_with(line, "STATUS") || starts_with(line, "?")) {
        report_status();
        return;
    }

    if (starts_with(line, "LIMITS")) {
        report_limits();
        return;
    }

    if (starts_with(line, "ROUTE")) {
        command_set_route(line);
        return;
    }

    if (starts_with(line, "CELLS")) {
        report_all_cells();
        return;
    }

    if (starts_with(line, "CELL ")) {
        command_set_cell(line);
        return;
    }

    if (starts_with(line, "PATH ")) {
        command_set_path(line);
        return;
    }

    if (starts_with(line, "APPLYROUTE")) {
        command_apply_route();
        return;
    }

    if (starts_with(line, "HOME")) {
        home_start();
        return;
    }

    if (starts_with(line, "AUTO")) {
        auto_start();
        return;
    }

    if (starts_with(line, "STOP") || starts_with(line, "JOGC")) {
        stop_all();
        return;
    }

    if (starts_with(line, "CLEAR")) {
        clear_alarm();
        return;
    }

    if (starts_with(line, "COUNTRESET")) {
        product_count = 0;
        SWL("count:reset");
        report_status();
        return;
    }

    if (starts_with(line, "STEP")) {
        manual_step_mm = parse_number_after_space(line, manual_step_mm);
        if (manual_step_mm <= 0.0f) manual_step_mm = 1.0f;
        SWL("step:ok");
        report_status();
        return;
    }

    if (starts_with(line, "FEEDZ")) {
        auto_feed_z_mm_min = parse_number_after_space(line, auto_feed_z_mm_min);
        if (auto_feed_z_mm_min <= 0.0f) auto_feed_z_mm_min = 1800.0f;
        SWL("feedz:ok");
        report_status();
        return;
    }

    if (starts_with(line, "FEEDXY")) {
        /*
         * Backward compatibility.
         * XY speed is shared by Jog XY and Auto XY.
         */
        auto_feed_xy_mm_min = parse_number_after_space(line, auto_feed_xy_mm_min);
        if (auto_feed_xy_mm_min <= 0.0f) auto_feed_xy_mm_min = 6000.0f;
        manual_feed_mm_min = auto_feed_xy_mm_min;
        SWL("feedxy:ok");
        report_status();
        return;
    }

    if (starts_with(line, "FEED")) {
        /*
         * Main XY speed.
         * One value controls Jog X/Y and Auto X/Y.
         */
        manual_feed_mm_min = parse_number_after_space(line, manual_feed_mm_min);
        if (manual_feed_mm_min <= 0.0f) manual_feed_mm_min = 6000.0f;
        auto_feed_xy_mm_min = manual_feed_mm_min;
        SWL("feed:ok");
        report_status();
        return;
    }

    if (starts_with(line, "FANON")) {
        fan_on();
        SWL("fan:on");
        report_status();
        return;
    }

    if (starts_with(line, "FANOFF")) {
        fan_off();
        SWL("fan:off");
        report_status();
        return;
    }

    if (starts_with(line, "MAGON")) {
        magnet_on();
        SWL("magnet:on");
        report_status();
        return;
    }

    if (starts_with(line, "MAGOFF")) {
        magnet_off();
        SWL("magnet:off");
        report_status();
        return;
    }

    if (starts_with(line, "HOMESET")) {
        stop_motion();
        st_set_position_mm(0.0f, 0.0f, 0.0f);
        sync_planner_to_current_position();

        is_homed = 1;
        run_mode = MODE_IDLE;
        auto_current_cell = CELL_HOME;

        SWL("homeset:ok");
        report_status();
        return;
    }

    if (starts_with(line, "GOTO") || starts_with(line, "JOGA")) {
        command_goto(line);
        return;
    }

    if (starts_with(line, "JOGI")) {
        command_jogi(line);
        return;
    }

    if (starts_with(line, "X+")) {
        r = start_jog_axis(X_AXIS, DIR_POSITIVE);
        jog_result(r);
        return;
    }

    if (starts_with(line, "X-")) {
        r = start_jog_axis(X_AXIS, DIR_NEGATIVE);
        jog_result(r);
        return;
    }

    if (starts_with(line, "Y+")) {
        r = start_jog_axis(Y_AXIS, DIR_POSITIVE);
        jog_result(r);
        return;
    }

    if (starts_with(line, "Y-")) {
        r = start_jog_axis(Y_AXIS, DIR_NEGATIVE);
        jog_result(r);
        return;
    }

    if (starts_with(line, "Z+")) {
        r = start_jog_axis(Z_AXIS, DIR_POSITIVE);
        jog_result(r);
        return;
    }

    if (starts_with(line, "Z-")) {
        r = start_jog_axis(Z_AXIS, DIR_NEGATIVE);
        jog_result(r);
        return;
    }

    SWL("error:unknown_command");
}

static void protocol_process_serial(void)
{
    while (serial_available()) {
        uint8_t c = serial_read();

        if (c == SERIAL_NO_DATA) {
            return;
        }

        if (c == '\r' || c == '\n') {
            if (line_index > 0) {
                line_buffer[line_index] = 0;
                process_command(line_buffer);
                line_index = 0;
            }
        } else {
            if (line_index < (LINE_BUFFER_SIZE - 1)) {
                line_buffer[line_index++] = (char)c;
            } else {
                line_index = 0;
                SWL("error:line_overflow");
            }
        }
    }
}

/* =========================================================
 * Main
 * ========================================================= */


/* =========================================================
 * Public application entry points
 * ========================================================= */

 //khoi tao ngoai vi
void gantry_init(void)
{
    serial_init(115200);

    sensor_init();
    relay_init();
    lcd_init();

    plan_init();
    stepper_init();

    limits_init();
    limits_disable();

    st_set_position_mm(0.0f, 0.0f, 0.0f);
    sync_planner_to_current_position();

    outputs_init_off();

    sei();

    _delay_ms(1000);

    lcd_clear();
    lcd_update();

    SWL("");
    SWL("gantry:integrated_ready");
    SWL("cmd: HOME AUTO STOP STATUS CELLS CELL PATH X+ X- Y+ Y- Z+ Z- FANON FANOFF MAGON MAGOFF");
    SWL("lcd: english only");

    report_all_cells();
    report_limits();
    report_status();
}


//1 vong chu trinh thuc hien cua he
void gantry_loop_once(void)
{
    sensor_state = sensor_object_detected();

    protocol_process_serial();

    if (run_mode == MODE_HOMING) {
        home_process();
        return;
    }

    st_prep_buffer();

    /*
        * AUTO may be armed while State = Idle at HOME waiting for object.
        * Therefore call auto_process() whenever auto_enabled = 1,
        * not only when run_mode == MODE_AUTO.
        */
    if (auto_enabled) {
        auto_process();
    }

    /*
        * Limit checking is safe to call in all non-homing states.
        * If no motor is running, it returns immediately.
        */
    check_limit_while_running();

    uint8_t running = st_is_running();

    if (last_running && !running) {
        if (run_mode == MODE_JOG) {
            current_axis = AXIS_NONE;
            current_dir = 0;
            run_mode = MODE_IDLE;
            SWL("jog:done");
            report_status();
        } else if (run_mode == MODE_AUTO) {
            report_status();
        }
    }

    last_running = running;

    status_counter++;
    if (status_counter >= STATUS_PERIOD_LOOP) {
        status_counter = 0;
        report_status();
        report_limits();
    }

    lcd_counter++;
    if (lcd_counter >= LCD_PERIOD_LOOP) {
        lcd_counter = 0;
        lcd_update();
    }

    _delay_ms(10);
}
