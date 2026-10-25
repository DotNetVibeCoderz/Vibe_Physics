/*
 * Chrono.Net native shim — Chrono::Vehicle bindings (wheeled + tracked models, rigid & SCM terrain).
 * Vehicle worlds are Z-up (Chrono::Vehicle ISO convention).
 * Made by Gravicode Studios, led by Kang Fadhil.
 */
#ifndef CHRONO_NATIVE_VEHICLE_H
#define CHRONO_NATIVE_VEHICLE_H

#include "chrono_native.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef enum {
    CHN_VEHICLE_HMMWV = 0,
    CHN_VEHICLE_SEDAN = 1,
    CHN_VEHICLE_GATOR = 2,
    CHN_VEHICLE_UAZBUS = 3,
    CHN_VEHICLE_CITYBUS = 4,
    CHN_VEHICLE_M113 = 100  /* tracked */
} chn_vehicle_model;

typedef enum { CHN_TIRE_RIGID = 0, CHN_TIRE_TMEASY = 1, CHN_TIRE_FIALA = 2, CHN_TIRE_PAC02 = 3 } chn_tire_model;

typedef struct {
    double time;
    double speed;          /* m/s, forward */
    double engine_rpm;
    double engine_torque;  /* N.m */
    int32_t gear;
    int32_t _pad;
    chn_vec3 chassis_pos;
    chn_quat chassis_rot;
    double steering;
    double throttle;
    double braking;
} chn_vehicle_telemetry;

typedef struct chn_vehicle_t chn_vehicle;
typedef struct chn_terrain_t chn_terrain;

CHN_API int32_t chn_vehicle_supported(void);
CHN_API chn_vehicle* chn_vehicle_create(int32_t model, chn_vec3 init_pos, double init_yaw, int32_t contact_method,
                                        int32_t tire_model, double step_size);
CHN_API void chn_vehicle_release(chn_vehicle* vehicle);
/* Returns a system handle sharing ownership with the vehicle (release it with chn_system_release). */
CHN_API chn_system* chn_vehicle_get_system(chn_vehicle* vehicle);
CHN_API chn_body* chn_vehicle_get_chassis(chn_vehicle* vehicle);
CHN_API int32_t chn_vehicle_step(chn_vehicle* vehicle, chn_terrain* terrain, double steering, double throttle,
                                 double braking, double dt);
CHN_API int32_t chn_vehicle_get_telemetry(chn_vehicle* vehicle, chn_vehicle_telemetry* out);
CHN_API int32_t chn_vehicle_is_tracked(chn_vehicle* vehicle);

CHN_API chn_terrain* chn_terrain_create_rigid(chn_system* sys);
CHN_API int32_t chn_terrain_rigid_add_patch(chn_terrain* terrain, chn_frame frame, double length, double width,
                                            double thickness, float friction, float restitution);
CHN_API chn_terrain* chn_terrain_create_scm(chn_system* sys, double size_x, double size_y, double resolution,
                                            double bekker_kphi, double bekker_kc, double bekker_n, double cohesion,
                                            double friction_deg, double janosi, double elastic_k, double damping_r);
CHN_API int32_t chn_terrain_initialize(chn_terrain* terrain);
CHN_API double chn_terrain_get_height(chn_terrain* terrain, chn_vec3 location);
/* SCM only: deformed surface mesh (two-phase query, see chn_body_get_visual_mesh). */
CHN_API int32_t chn_terrain_get_mesh(chn_terrain* terrain, chn_vec3* vertices, int32_t* nvert, int32_t* indices,
                                     int32_t* ntri);
CHN_API void chn_terrain_release(chn_terrain* terrain);

#ifdef __cplusplus
}
#endif

#endif /* CHRONO_NATIVE_VEHICLE_H */
