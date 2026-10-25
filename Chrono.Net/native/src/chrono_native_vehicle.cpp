// Chrono.Net native shim — Chrono::Vehicle bindings.
// Made by Gravicode Studios, led by Kang Fadhil.

#include "chrono_native_vehicle.h"
#include "chrono_native_internal.h"

#include <functional>

#include "chrono_vehicle/ChVehicle.h"
#include "chrono_vehicle/ChWorldFrame.h"
#include "chrono_vehicle/terrain/RigidTerrain.h"
#include "chrono_vehicle/terrain/SCMTerrain.h"
#include "chrono_models/vehicle/hmmwv/HMMWV.h"
#include "chrono_models/vehicle/sedan/Sedan.h"
#include "chrono_models/vehicle/gator/Gator.h"
#include "chrono_models/vehicle/uaz/UAZBUS.h"
#include "chrono_models/vehicle/citybus/CityBus.h"
#include "chrono_models/vehicle/m113/M113.h"

using namespace chrono;
using namespace chrono::vehicle;
using namespace chrono_native;

#define CHN_GUARD_BEGIN try {
#define CHN_GUARD_END(ret)                     \
    }                                          \
    catch (const std::exception& e) {          \
        set_error(e.what());                   \
        return ret;                            \
    }                                          \
    catch (...) {                              \
        set_error("unknown native exception"); \
        return ret;                            \
    }

struct chn_vehicle_t {
    std::shared_ptr<void> model;  // owns the concrete model (and through it, the ChSystem)
    ChVehicle* vehicle = nullptr;
    ChSystem* system = nullptr;
    bool tracked = false;
    std::function<void(double, const DriverInputs&, ChTerrain&)> synchronize;
    std::function<void(double)> advance;
    DriverInputs last_inputs{0, 0, 0, 0};
};

struct chn_terrain_t {
    std::shared_ptr<ChTerrain> ptr;
    RigidTerrain* rigid = nullptr;
    SCMTerrain* scm = nullptr;
    ChSystem* system = nullptr;
};

namespace {

TireModelType to_tire(int32_t t) {
    switch (t) {
        case CHN_TIRE_RIGID: return TireModelType::RIGID;
        case CHN_TIRE_FIALA: return TireModelType::FIALA;
        case CHN_TIRE_PAC02: return TireModelType::PAC02;
        default: return TireModelType::TMEASY;
    }
}

template <typename M>
chn_vehicle* make_wheeled(std::shared_ptr<M> m, const ChCoordsys<>& init, ChContactMethod method, int32_t tire,
                          double step) {
    m->SetContactMethod(method);
    m->SetCollisionSystemType(ChCollisionSystem::Type::BULLET);
    m->SetChassisCollisionType(CollisionType::NONE);
    m->SetInitPosition(init);
    m->SetTireType(to_tire(tire));
    m->SetTireStepSize(step);
    m->Initialize();
    m->SetChassisVisualizationType(VisualizationType::PRIMITIVES);
    m->SetSuspensionVisualizationType(VisualizationType::PRIMITIVES);
    m->SetSteeringVisualizationType(VisualizationType::PRIMITIVES);
    m->SetWheelVisualizationType(VisualizationType::PRIMITIVES);
    m->SetTireVisualizationType(VisualizationType::PRIMITIVES);

    auto* h = new chn_vehicle_t();
    h->model = m;
    h->vehicle = &m->GetVehicle();
    h->system = m->GetSystem();
    h->synchronize = [m](double t, const DriverInputs& in, ChTerrain& terrain) { m->Synchronize(t, in, terrain); };
    h->advance = [m](double dt) { m->Advance(dt); };
    return h;
}

}  // namespace

extern "C" {

int32_t chn_vehicle_supported(void) {
    return 1;
}

chn_vehicle* chn_vehicle_create(int32_t model, chn_vec3 init_pos, double init_yaw, int32_t contact_method,
                                int32_t tire_model, double step_size) {
    CHN_GUARD_BEGIN
    ChCoordsys<> init(to_ch(init_pos), QuatFromAngleZ(init_yaw));
    ChContactMethod method = contact_method == CHN_CONTACT_SMC ? ChContactMethod::SMC : ChContactMethod::NSC;
    switch (model) {
        case CHN_VEHICLE_HMMWV: {
            auto m = std::make_shared<hmmwv::HMMWV_Full>();
            m->SetEngineType(EngineModelType::SIMPLE_MAP);
            m->SetTransmissionType(TransmissionModelType::AUTOMATIC_SIMPLE_MAP);
            m->SetDriveType(DrivelineTypeWV::AWD);
            return make_wheeled(m, init, method, tire_model, step_size);
        }
        case CHN_VEHICLE_SEDAN: return make_wheeled(std::make_shared<sedan::Sedan>(), init, method, tire_model, step_size);
        case CHN_VEHICLE_GATOR: return make_wheeled(std::make_shared<gator::Gator>(), init, method, tire_model, step_size);
        case CHN_VEHICLE_UAZBUS: return make_wheeled(std::make_shared<uaz::UAZBUS>(), init, method, tire_model, step_size);
        case CHN_VEHICLE_CITYBUS: return make_wheeled(std::make_shared<citybus::CityBus>(), init, method, tire_model, step_size);
        case CHN_VEHICLE_M113: {
            auto m = std::make_shared<m113::M113>();
            m->SetContactMethod(method);
            m->SetCollisionSystemType(ChCollisionSystem::Type::BULLET);
            m->SetTrackShoeType(TrackShoeType::SINGLE_PIN);
            m->SetDrivelineType(DrivelineTypeTV::BDS);
            m->SetBrakeType(BrakeType::SIMPLE);
            m->SetEngineType(EngineModelType::SIMPLE_MAP);
            m->SetTransmissionType(TransmissionModelType::AUTOMATIC_SIMPLE_MAP);
            m->SetChassisCollisionType(CollisionType::NONE);
            m->SetInitPosition(init);
            m->Initialize();
            m->SetChassisVisualizationType(VisualizationType::PRIMITIVES);
            m->SetSprocketVisualizationType(VisualizationType::PRIMITIVES);
            m->SetIdlerVisualizationType(VisualizationType::PRIMITIVES);
            m->SetIdlerWheelVisualizationType(VisualizationType::PRIMITIVES);
            m->SetSuspensionVisualizationType(VisualizationType::PRIMITIVES);
            m->SetRoadWheelVisualizationType(VisualizationType::PRIMITIVES);
            m->SetTrackShoeVisualizationType(VisualizationType::PRIMITIVES);
            auto* h = new chn_vehicle_t();
            h->model = m;
            h->vehicle = &m->GetVehicle();
            h->system = m->GetSystem();
            h->tracked = true;
            h->synchronize = [m](double t, const DriverInputs& in, ChTerrain&) { m->Synchronize(t, in); };
            h->advance = [m](double dt) { m->Advance(dt); };
            return h;
        }
        default: set_error("unknown vehicle model"); return nullptr;
    }
    CHN_GUARD_END(nullptr)
}

void chn_vehicle_release(chn_vehicle* vehicle) {
    delete vehicle;
}

chn_system* chn_vehicle_get_system(chn_vehicle* vehicle) {
    auto* h = new chn_system_t();
    // Aliasing constructor: the system handle keeps the whole vehicle model alive.
    h->ptr = std::shared_ptr<ChSystem>(vehicle->model, vehicle->system);
    return h;
}

chn_body* chn_vehicle_get_chassis(chn_vehicle* vehicle) {
    auto* h = new chn_body_t();
    h->ptr = vehicle->vehicle->GetChassisBody();
    return h;
}

int32_t chn_vehicle_step(chn_vehicle* vehicle, chn_terrain* terrain, double steering, double throttle,
                         double braking, double dt) {
    CHN_GUARD_BEGIN
    if (!terrain) {
        set_error("terrain is required");
        return -1;
    }
    double t = vehicle->system->GetChTime();
    DriverInputs in{steering, throttle, braking, 0};
    vehicle->last_inputs = in;
    terrain->ptr->Synchronize(t);
    vehicle->synchronize(t, in, *terrain->ptr);
    terrain->ptr->Advance(dt);
    vehicle->advance(dt);
    return 0;
    CHN_GUARD_END(-1)
}

int32_t chn_vehicle_get_telemetry(chn_vehicle* vehicle, chn_vehicle_telemetry* out) {
    CHN_GUARD_BEGIN
    auto* v = vehicle->vehicle;
    out->time = vehicle->system->GetChTime();
    out->speed = v->GetSpeed();
    out->engine_rpm = 0;
    out->engine_torque = 0;
    out->gear = 0;
    out->_pad = 0;
    if (auto engine = v->GetEngine()) {
        out->engine_rpm = engine->GetMotorSpeed() * 60.0 / CH_2PI;
        out->engine_torque = engine->GetOutputMotorshaftTorque();
    }
    if (auto tr = v->GetTransmission())
        out->gear = tr->GetCurrentGear();
    auto chassis = v->GetChassisBody();
    out->chassis_pos = to_c(chassis->GetPos());
    out->chassis_rot = to_c(chassis->GetRot());
    out->steering = vehicle->last_inputs.m_steering;
    out->throttle = vehicle->last_inputs.m_throttle;
    out->braking = vehicle->last_inputs.m_braking;
    return 0;
    CHN_GUARD_END(-1)
}

int32_t chn_vehicle_is_tracked(chn_vehicle* vehicle) {
    return vehicle->tracked ? 1 : 0;
}

chn_terrain* chn_terrain_create_rigid(chn_system* sys) {
    CHN_GUARD_BEGIN
    auto t = std::make_shared<RigidTerrain>(sys->ptr.get());
    auto* h = new chn_terrain_t();
    h->rigid = t.get();
    h->system = sys->ptr.get();
    h->ptr = t;
    return h;
    CHN_GUARD_END(nullptr)
}

int32_t chn_terrain_rigid_add_patch(chn_terrain* terrain, chn_frame frame, double length, double width,
                                    double thickness, float friction, float restitution) {
    CHN_GUARD_BEGIN
    if (!terrain->rigid) {
        set_error("not a rigid terrain");
        return -1;
    }
    ChContactMaterialData minfo;
    minfo.mu = friction;
    minfo.cr = restitution;
    minfo.Y = 2e7f;
    auto mat = minfo.CreateMaterial(terrain->system->GetContactMethod());
    ChFramed f = to_ch(frame);
    auto patch = terrain->rigid->AddPatch(mat, ChCoordsys<>(f.GetPos(), f.GetRot()), length, width, thickness);
    patch->SetColor(ChColor(0.42f, 0.45f, 0.40f));
    return 0;
    CHN_GUARD_END(-1)
}

chn_terrain* chn_terrain_create_scm(chn_system* sys, double size_x, double size_y, double resolution,
                                    double bekker_kphi, double bekker_kc, double bekker_n, double cohesion,
                                    double friction_deg, double janosi, double elastic_k, double damping_r) {
    CHN_GUARD_BEGIN
    auto t = std::make_shared<SCMTerrain>(sys->ptr.get());
    t->SetSoilParameters(bekker_kphi, bekker_kc, bekker_n, cohesion, friction_deg, janosi, elastic_k, damping_r);
    t->Initialize(size_x, size_y, resolution);
    auto* h = new chn_terrain_t();
    h->scm = t.get();
    h->system = sys->ptr.get();
    h->ptr = t;
    return h;
    CHN_GUARD_END(nullptr)
}

int32_t chn_terrain_initialize(chn_terrain* terrain) {
    CHN_GUARD_BEGIN
    if (terrain->rigid)
        terrain->rigid->Initialize();
    return 0;
    CHN_GUARD_END(-1)
}

double chn_terrain_get_height(chn_terrain* terrain, chn_vec3 location) {
    try {
        return terrain->ptr->GetHeight(to_ch(location));
    } catch (...) {
        return 0;
    }
}

int32_t chn_terrain_get_mesh(chn_terrain* terrain, chn_vec3* vertices, int32_t* nvert, int32_t* indices,
                             int32_t* ntri) {
    CHN_GUARD_BEGIN
    if (!terrain->scm) {
        set_error("terrain has no deformable mesh");
        return -1;
    }
    auto mesh = terrain->scm->GetMesh()->GetMesh();
    const auto& v = mesh->GetCoordsVertices();
    const auto& f = mesh->GetIndicesVertexes();
    if (vertices && indices) {
        if (*nvert < (int32_t)v.size() || *ntri < (int32_t)f.size()) {
            set_error("buffer too small");
            return -1;
        }
        for (size_t i = 0; i < v.size(); i++)
            vertices[i] = to_c(v[i]);
        for (size_t i = 0; i < f.size(); i++) {
            indices[3 * i] = f[i].x();
            indices[3 * i + 1] = f[i].y();
            indices[3 * i + 2] = f[i].z();
        }
    }
    *nvert = (int32_t)v.size();
    *ntri = (int32_t)f.size();
    return 0;
    CHN_GUARD_END(-1)
}

void chn_terrain_release(chn_terrain* terrain) {
    delete terrain;
}

}  // extern "C"
