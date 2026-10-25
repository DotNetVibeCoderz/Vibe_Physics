/*
 * Chrono.Net native shim — a flat C ABI over Project Chrono (C++).
 *
 * Design rules:
 *  - Every object is an opaque handle owning one std::shared_ptr reference. Release with the matching *_release().
 *  - Functions never throw across the boundary. Failures return a negative status (or NULL handle) and the message
 *    is available through chn_last_error() (thread-local).
 *  - All math is double precision, blittable (chn_vec3 / chn_quat) so the managed side can pass spans with no copies.
 *  - Quaternions follow Chrono's convention: e0 = scalar (w), e1..e3 = vector (x, y, z).
 *
 * Made by Gravicode Studios, led by Kang Fadhil.
 */
#ifndef CHRONO_NATIVE_H
#define CHRONO_NATIVE_H

#include <stdint.h>

#ifdef _WIN32
#define CHN_API __declspec(dllexport)
#else
#define CHN_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define CHN_ABI_VERSION 1

/* ---------------------------------------------------------------- types */

typedef struct { double x, y, z; } chn_vec3;
typedef struct { double e0, e1, e2, e3; } chn_quat;
typedef struct { chn_vec3 pos; chn_quat rot; } chn_frame;

/* Per-body kinematic snapshot (written in bulk by chn_system_get_body_states). */
typedef struct {
    chn_vec3 pos;      /* center of mass, absolute */
    chn_quat rot;      /* COM frame orientation */
    chn_vec3 lin_vel;  /* absolute */
    chn_vec3 ang_vel;  /* expressed in absolute frame */
    chn_vec3 vis_pos;  /* visual/reference frame (differs from COM for aux-ref bodies) */
    chn_quat vis_rot;
} chn_body_state;

typedef struct {
    chn_vec3 point_a;
    chn_vec3 point_b;
    chn_vec3 normal;   /* contact normal (X axis of contact plane), absolute */
    chn_vec3 force;    /* reaction force, absolute */
    double distance;   /* negative when penetrating */
} chn_contact;

typedef enum {
    CHN_SHAPE_BOX = 0,       /* dims = full lengths (x,y,z) */
    CHN_SHAPE_SPHERE = 1,    /* dims.x = radius */
    CHN_SHAPE_CYLINDER = 2,  /* dims.x = radius, dims.y = height (along local Z, centered) */
    CHN_SHAPE_CAPSULE = 3,   /* dims.x = radius, dims.y = height of cylindrical part (along local Z) */
    CHN_SHAPE_CONE = 4,      /* dims.x = radius, dims.y = height (along local Z, centered: base at -h/2, apex at +h/2) */
    CHN_SHAPE_ELLIPSOID = 5, /* dims = semi-axes */
    CHN_SHAPE_MESH = 6,      /* use chn_body_get_visual_mesh */
    CHN_SHAPE_OTHER = 99
} chn_shape_type;

typedef struct {
    int32_t type;      /* chn_shape_type */
    int32_t index;     /* index of the shape inside the body's visual model */
    chn_vec3 dims;
    chn_frame frame;   /* relative to the body visual frame */
    float r, g, b, a;
} chn_visual_shape;

typedef enum { CHN_CONTACT_NSC = 0, CHN_CONTACT_SMC = 1 } chn_contact_method;

typedef enum {
    CHN_LINK_LOCK = 0,
    CHN_LINK_REVOLUTE = 1,
    CHN_LINK_SPHERICAL = 2,
    CHN_LINK_CYLINDRICAL = 3,
    CHN_LINK_PRISMATIC = 4,
    CHN_LINK_POINT_PLANE = 5,
    CHN_LINK_POINT_LINE = 6,
    CHN_LINK_PLANAR = 7,
    CHN_LINK_OLDHAM = 8,
    CHN_LINK_UNIVERSAL = 9,
    CHN_LINK_PARALLEL = 10,
    CHN_LINK_ORTHOGONAL = 11
} chn_link_kind;

typedef enum {
    CHN_MOTOR_ROT_ANGLE = 0,
    CHN_MOTOR_ROT_SPEED = 1,
    CHN_MOTOR_ROT_TORQUE = 2,
    CHN_MOTOR_LIN_POSITION = 3,
    CHN_MOTOR_LIN_SPEED = 4,
    CHN_MOTOR_LIN_FORCE = 5
} chn_motor_kind;

typedef struct chn_system_t chn_system;
typedef struct chn_body_t chn_body;
typedef struct chn_link_t chn_link;
typedef struct chn_material_t chn_material;
typedef struct chn_function_t chn_function;
typedef struct chn_mesh_t chn_mesh;

/* Callbacks. `user` is an opaque pointer supplied at registration (a GCHandle on the managed side). */
typedef double (*chn_function_cb)(double x, void* user);
typedef double (*chn_tsda_force_cb)(double time, double rest_length, double length, double vel, void* user);
typedef double (*chn_rsda_torque_cb)(double time, double rest_angle, double angle, double vel, void* user);

/* ---------------------------------------------------------------- library */

CHN_API int32_t chn_abi_version(void);
CHN_API const char* chn_chrono_version(void);
/* Copies the last error message for the calling thread; returns its full length. */
CHN_API int32_t chn_last_error(char* buffer, int32_t capacity);
CHN_API void chn_set_data_path(const char* path);
CHN_API void chn_set_default_envelope(double envelope, double margin);

/* ---------------------------------------------------------------- system */

CHN_API chn_system* chn_system_create(int32_t contact_method);
CHN_API void chn_system_release(chn_system* sys);
CHN_API int32_t chn_system_contact_method(chn_system* sys);
CHN_API void chn_system_set_gravity(chn_system* sys, chn_vec3 g);
CHN_API chn_vec3 chn_system_get_gravity(chn_system* sys);
CHN_API int32_t chn_system_set_solver(chn_system* sys, int32_t solver_type, int32_t max_iterations, double tolerance);
CHN_API int32_t chn_system_set_timestepper(chn_system* sys, int32_t stepper_type);
CHN_API int32_t chn_system_set_collision_system(chn_system* sys, int32_t type);
CHN_API void chn_system_set_num_threads(chn_system* sys, int32_t chrono, int32_t collision, int32_t eigen);
CHN_API void chn_system_set_max_penetration_recovery_speed(chn_system* sys, double v);
CHN_API int32_t chn_system_step(chn_system* sys, double dt);
/* Advances `count` steps of size dt in one native call (avoids per-step transitions). */
CHN_API int32_t chn_system_step_many(chn_system* sys, double dt, int32_t count);
CHN_API int32_t chn_system_do_static_linear(chn_system* sys);
CHN_API int32_t chn_system_do_static_nonlinear(chn_system* sys, int32_t steps);
CHN_API int32_t chn_system_do_assembly(chn_system* sys);
CHN_API double chn_system_get_time(chn_system* sys);
CHN_API void chn_system_set_time(chn_system* sys, double t);
CHN_API double chn_system_get_step_timer(chn_system* sys);
CHN_API int32_t chn_system_num_contacts(chn_system* sys);
CHN_API int32_t chn_system_num_bodies(chn_system* sys);
CHN_API int32_t chn_system_num_links(chn_system* sys);
CHN_API int32_t chn_system_add_body(chn_system* sys, chn_body* body);
CHN_API int32_t chn_system_remove_body(chn_system* sys, chn_body* body);
CHN_API int32_t chn_system_add_link(chn_system* sys, chn_link* link);
CHN_API int32_t chn_system_remove_link(chn_system* sys, chn_link* link);
CHN_API int32_t chn_system_add_mesh(chn_system* sys, chn_mesh* mesh);
/* New handle to the i-th body in system order (e.g. bodies created internally by vehicle models). */
CHN_API chn_body* chn_system_get_body(chn_system* sys, int32_t index);
/* Writes states of the first `capacity` bodies in system order; returns number of bodies in the system. */
CHN_API int32_t chn_system_get_body_states(chn_system* sys, chn_body_state* out, int32_t capacity);
CHN_API int32_t chn_system_get_contacts(chn_system* sys, chn_contact* out, int32_t capacity);
/* Full state vector access (positions + velocities) for checkpoint / RL episode reset. */
CHN_API int32_t chn_system_state_size(chn_system* sys, int32_t* npos, int32_t* nvel);
CHN_API int32_t chn_system_state_get(chn_system* sys, double* pos, int32_t npos, double* vel, int32_t nvel, double* time);
CHN_API int32_t chn_system_state_set(chn_system* sys, const double* pos, int32_t npos, const double* vel, int32_t nvel, double time);

/* ---------------------------------------------------------------- materials */

CHN_API chn_material* chn_material_create(int32_t contact_method);
CHN_API void chn_material_release(chn_material* mat);
CHN_API void chn_material_set_friction(chn_material* mat, float friction);
CHN_API void chn_material_set_restitution(chn_material* mat, float restitution);
CHN_API void chn_material_set_rolling_friction(chn_material* mat, float v);
CHN_API void chn_material_set_spinning_friction(chn_material* mat, float v);
CHN_API void chn_material_set_compliance(chn_material* mat, float v);       /* NSC */
CHN_API void chn_material_set_cohesion(chn_material* mat, float v);         /* NSC */
CHN_API void chn_material_set_young_modulus(chn_material* mat, float v);    /* SMC */
CHN_API void chn_material_set_poisson_ratio(chn_material* mat, float v);    /* SMC */
CHN_API void chn_material_set_adhesion(chn_material* mat, float v);         /* SMC */

/* ---------------------------------------------------------------- bodies */

CHN_API chn_body* chn_body_create(void);
CHN_API chn_body* chn_body_create_box(chn_vec3 size, double density, int32_t visual, int32_t collide, chn_material* mat);
CHN_API chn_body* chn_body_create_sphere(double radius, double density, int32_t visual, int32_t collide, chn_material* mat);
/* axis: 0=X, 1=Y, 2=Z */
CHN_API chn_body* chn_body_create_cylinder(int32_t axis, double radius, double height, double density, int32_t visual, int32_t collide, chn_material* mat);
CHN_API chn_body* chn_body_create_ellipsoid(chn_vec3 semi_axes, double density, int32_t visual, int32_t collide, chn_material* mat);
CHN_API chn_body* chn_body_create_convex_hull(const chn_vec3* points, int32_t count, double density, int32_t visual, int32_t collide, chn_material* mat);
CHN_API chn_body* chn_body_create_mesh(const chn_vec3* vertices, int32_t nvert, const int32_t* indices, int32_t ntri,
                                       double density, int32_t compute_mass, int32_t visual, int32_t collide,
                                       chn_material* mat, double sphere_swept);
CHN_API void chn_body_release(chn_body* body);

CHN_API void chn_body_set_name(chn_body* body, const char* name);
CHN_API int32_t chn_body_get_name(chn_body* body, char* buffer, int32_t capacity);
CHN_API void chn_body_set_pos(chn_body* body, chn_vec3 p);
CHN_API chn_vec3 chn_body_get_pos(chn_body* body);
CHN_API void chn_body_set_rot(chn_body* body, chn_quat q);
CHN_API chn_quat chn_body_get_rot(chn_body* body);
CHN_API void chn_body_set_lin_vel(chn_body* body, chn_vec3 v);
CHN_API chn_vec3 chn_body_get_lin_vel(chn_body* body);
CHN_API void chn_body_set_ang_vel(chn_body* body, chn_vec3 w);    /* absolute frame */
CHN_API chn_vec3 chn_body_get_ang_vel(chn_body* body);            /* absolute frame */
CHN_API chn_vec3 chn_body_get_lin_acc(chn_body* body);
CHN_API void chn_body_get_state(chn_body* body, chn_body_state* out);
CHN_API void chn_body_set_mass(chn_body* body, double mass);
CHN_API double chn_body_get_mass(chn_body* body);
CHN_API void chn_body_set_inertia_xx(chn_body* body, chn_vec3 ixx);
CHN_API chn_vec3 chn_body_get_inertia_xx(chn_body* body);
CHN_API void chn_body_set_inertia_xy(chn_body* body, chn_vec3 ixy);
CHN_API void chn_body_set_fixed(chn_body* body, int32_t fixed);
CHN_API int32_t chn_body_is_fixed(chn_body* body);
CHN_API void chn_body_enable_collision(chn_body* body, int32_t enable);
CHN_API int32_t chn_body_collision_enabled(chn_body* body);
CHN_API void chn_body_set_sleeping_allowed(chn_body* body, int32_t allowed);
CHN_API chn_vec3 chn_body_get_contact_force(chn_body* body);
CHN_API chn_vec3 chn_body_get_contact_torque(chn_body* body);

/* External loads: a per-body accumulator owned by the shim. Clear once per step, then accumulate. */
CHN_API void chn_body_clear_forces(chn_body* body);
CHN_API void chn_body_add_force(chn_body* body, chn_vec3 force, chn_vec3 point, int32_t local);
CHN_API void chn_body_add_torque(chn_body* body, chn_vec3 torque, int32_t local);

/* Collision shapes (local frame relative to the body). */
CHN_API int32_t chn_body_add_collision_box(chn_body* body, chn_material* mat, chn_vec3 size, chn_frame frame);
CHN_API int32_t chn_body_add_collision_sphere(chn_body* body, chn_material* mat, double radius, chn_frame frame);
CHN_API int32_t chn_body_add_collision_cylinder(chn_body* body, chn_material* mat, double radius, double height, chn_frame frame);
CHN_API int32_t chn_body_add_collision_capsule(chn_body* body, chn_material* mat, double radius, double height, chn_frame frame);
CHN_API int32_t chn_body_add_collision_cone(chn_body* body, chn_material* mat, double radius, double height, chn_frame frame);
CHN_API int32_t chn_body_add_collision_mesh(chn_body* body, chn_material* mat, const chn_vec3* vertices, int32_t nvert,
                                            const int32_t* indices, int32_t ntri, int32_t is_static, int32_t is_convex,
                                            double sphere_swept, chn_frame frame);

/* Visual shapes (for renderers). Color components are 0..1. */
CHN_API int32_t chn_body_add_visual_box(chn_body* body, chn_vec3 size, chn_frame frame, float r, float g, float b);
CHN_API int32_t chn_body_add_visual_sphere(chn_body* body, double radius, chn_frame frame, float r, float g, float b);
CHN_API int32_t chn_body_add_visual_cylinder(chn_body* body, double radius, double height, chn_frame frame, float r, float g, float b);
CHN_API int32_t chn_body_add_visual_capsule(chn_body* body, double radius, double height, chn_frame frame, float r, float g, float b);
CHN_API int32_t chn_body_add_visual_cone(chn_body* body, double radius, double height, chn_frame frame, float r, float g, float b);
CHN_API int32_t chn_body_set_color(chn_body* body, float r, float g, float b);
CHN_API int32_t chn_body_get_visual_shapes(chn_body* body, chn_visual_shape* out, int32_t capacity);
/* Two-phase query: call with NULL buffers to get counts, then with buffers. Returns 0 on success. */
CHN_API int32_t chn_body_get_visual_mesh(chn_body* body, int32_t shape_index, chn_vec3* vertices, int32_t* nvert,
                                         int32_t* indices, int32_t* ntri);

/* ---------------------------------------------------------------- functions (time laws) */

CHN_API chn_function* chn_function_const(double value);
CHN_API chn_function* chn_function_ramp(double y0, double slope);
CHN_API chn_function* chn_function_sine(double amplitude, double frequency, double phase, double shift);
CHN_API chn_function* chn_function_poly345(double height, double width);
CHN_API chn_function* chn_function_setpoint(void);
CHN_API void chn_function_setpoint_set(chn_function* fn, double value);
CHN_API chn_function* chn_function_callback(chn_function_cb cb, void* user);
CHN_API double chn_function_eval(chn_function* fn, double x);
CHN_API void chn_function_release(chn_function* fn);

/* ---------------------------------------------------------------- links */

/* Joint at an absolute frame. Revolute/cylindrical/prismatic act on the frame Z axis. */
CHN_API chn_link* chn_link_create_joint(int32_t kind, chn_body* body1, chn_body* body2, chn_frame frame);
CHN_API chn_link* chn_link_create_distance(chn_body* body1, chn_body* body2, chn_vec3 p1, chn_vec3 p2, int32_t local);
CHN_API chn_link* chn_link_create_gear(chn_body* body1, chn_body* body2, double ratio, int32_t epicyclic,
                                       chn_frame shaft1, chn_frame shaft2);
CHN_API chn_link* chn_link_create_rack_pinion(chn_body* pinion, chn_body* rack, double pinion_radius,
                                              chn_frame pinion_frame, chn_frame rack_frame);
CHN_API chn_link* chn_link_create_motor(int32_t kind, chn_body* body1, chn_body* body2, chn_frame frame, chn_function* fn);
CHN_API int32_t chn_motor_set_function(chn_link* link, chn_function* fn);
/* Outputs: rotation -> (angle, speed, torque); linear -> (position, speed, force). */
CHN_API int32_t chn_motor_get(chn_link* link, double* value, double* speed, double* effort);
CHN_API chn_link* chn_link_create_spring(chn_body* body1, chn_body* body2, chn_vec3 p1, chn_vec3 p2, int32_t local,
                                         double k, double c, double rest_length, double actuator_force);
CHN_API int32_t chn_spring_set_force_callback(chn_link* link, chn_tsda_force_cb cb, void* user);
/* length, velocity, force */
CHN_API int32_t chn_spring_get(chn_link* link, double* length, double* velocity, double* force);
CHN_API chn_link* chn_link_create_rot_spring(chn_body* body1, chn_body* body2, chn_frame frame, double k, double c,
                                             double rest_angle, double actuator_torque);
CHN_API int32_t chn_rot_spring_set_torque_callback(chn_link* link, chn_rsda_torque_cb cb, void* user);
CHN_API int32_t chn_rot_spring_get(chn_link* link, double* angle, double* velocity, double* torque);
CHN_API int32_t chn_link_set_limits(chn_link* link, int32_t axis, double min, double max); /* axis 0..5: x,y,z,rx,ry,rz */
CHN_API void chn_link_release(chn_link* link);
CHN_API void chn_link_set_name(chn_link* link, const char* name);
CHN_API int32_t chn_link_get_frames(chn_link* link, chn_frame* frame1_abs, chn_frame* frame2_abs);
/* Reaction on body2 at frame2, expressed in absolute coordinates. */
CHN_API int32_t chn_link_get_reaction(chn_link* link, chn_vec3* force, chn_vec3* torque);
CHN_API void chn_link_set_disabled(chn_link* link, int32_t disabled);

/* ---------------------------------------------------------------- FEA */

CHN_API chn_mesh* chn_mesh_create(void);
CHN_API void chn_mesh_release(chn_mesh* mesh);
CHN_API void chn_mesh_set_automatic_gravity(chn_mesh* mesh, int32_t enable);
/* Builds N Euler beam elements from A to B with a rectangular section. Returns index of first node. */
CHN_API int32_t chn_mesh_build_beam_euler(chn_mesh* mesh, chn_vec3 a, chn_vec3 b, chn_vec3 ydir, int32_t elements,
                                          double width_y, double width_z, double young, double shear, double density,
                                          double rayleigh_damping);
/* Builds N ANCF cable elements from A to B. Returns index of first node. */
CHN_API int32_t chn_mesh_build_cable_ancf(chn_mesh* mesh, chn_vec3 a, chn_vec3 b, int32_t elements, double diameter,
                                          double young, double density, double rayleigh_damping);
CHN_API int32_t chn_mesh_num_nodes(chn_mesh* mesh);
CHN_API int32_t chn_mesh_num_elements(chn_mesh* mesh);
CHN_API int32_t chn_mesh_get_node_positions(chn_mesh* mesh, chn_vec3* out, int32_t capacity);
/* Writes pairs of node indices for every 2-node (line) element; returns number of pairs. */
CHN_API int32_t chn_mesh_get_line_elements(chn_mesh* mesh, int32_t* out_pairs, int32_t capacity_pairs);
CHN_API int32_t chn_mesh_node_set_fixed(chn_mesh* mesh, int32_t node, int32_t fixed);
CHN_API int32_t chn_mesh_node_set_force(chn_mesh* mesh, int32_t node, chn_vec3 force);
CHN_API int32_t chn_mesh_node_attach(chn_system* sys, chn_mesh* mesh, int32_t node, chn_body* body);

#ifdef __cplusplus
}
#endif

#endif /* CHRONO_NATIVE_H */
