// Chrono.Net native shim — implementation of the flat C ABI declared in chrono_native.h.
// Made by Gravicode Studios, led by Kang Fadhil.

#include "chrono_native.h"
#include "chrono_native_internal.h"

#include <algorithm>
#include <cstring>
#include <unordered_map>

#include "chrono/ChVersion.h"
#include "chrono/core/ChDataPath.h"
#include "chrono/collision/ChCollisionModel.h"
#include "chrono/collision/ChCollisionShapes.h"
#include "chrono/assets/ChVisualShapes.h"
#include "chrono/functions/ChFunction.h"
#include "chrono/physics/ChBodyEasy.h"
#include "chrono/physics/ChContactContainer.h"
#include "chrono/physics/ChLinkDistance.h"
#include "chrono/physics/ChLinkLock.h"
#include "chrono/physics/ChLinkLockGear.h"
#include "chrono/physics/ChLinkMate.h"
#include "chrono/physics/ChLinkMotorAll.h"
#include "chrono/physics/ChLinkRSDA.h"
#include "chrono/physics/ChLinkTSDA.h"
#include "chrono/physics/ChLinkUniversal.h"
#include "chrono/solver/ChIterativeSolver.h"
#include "chrono/fea/ChBuilderBeam.h"
#include "chrono/fea/ChElementBeamEuler.h"
#include "chrono/fea/ChElementCableANCF.h"
#include "chrono/fea/ChLinkNodeFrame.h"
#include "chrono/fea/ChNodeFEAxyz.h"
#include "chrono/fea/ChNodeFEAxyzrot.h"

using namespace chrono;
using namespace chrono::fea;
using namespace chrono_native;

// ------------------------------------------------------------------------------------------------ error handling

namespace chrono_native {
thread_local std::string g_last_error;
void set_error(const char* msg) { g_last_error = msg ? msg : "unknown error"; }
}  // namespace chrono_native

#define CHN_GUARD_BEGIN try {
#define CHN_GUARD_END(ret)                         \
    }                                              \
    catch (const std::exception& e) {              \
        set_error(e.what());                       \
        return ret;                                \
    }                                              \
    catch (...) {                                  \
        set_error("unknown native exception");     \
        return ret;                                \
    }
#define CHN_GUARD_END_VOID                         \
    }                                              \
    catch (const std::exception& e) {              \
        set_error(e.what());                       \
    }                                              \
    catch (...) {                                  \
        set_error("unknown native exception");     \
    }

#define CHN_REQUIRE(cond, msg, ret) \
    if (!(cond)) {                  \
        set_error(msg);             \
        return ret;                 \
    }

// ------------------------------------------------------------------------------------------------ callbacks

namespace {

class CallbackFunction : public ChFunction {
  public:
    CallbackFunction(chn_function_cb cb, void* user) : m_cb(cb), m_user(user) {}
    CallbackFunction(const CallbackFunction& other) = default;
    virtual CallbackFunction* Clone() const override { return new CallbackFunction(*this); }
    virtual double GetVal(double x) const override { return m_cb(x, m_user); }

  private:
    chn_function_cb m_cb;
    void* m_user;
};

class CallbackForce : public ChLinkTSDA::ForceFunctor {
  public:
    CallbackForce(chn_tsda_force_cb cb, void* user) : m_cb(cb), m_user(user) {}
    virtual double evaluate(double time, double rest_length, double length, double vel, const ChLinkTSDA&) override {
        return m_cb(time, rest_length, length, vel, m_user);
    }

  private:
    chn_tsda_force_cb m_cb;
    void* m_user;
};

class CallbackTorque : public ChLinkRSDA::TorqueFunctor {
  public:
    CallbackTorque(chn_rsda_torque_cb cb, void* user) : m_cb(cb), m_user(user) {}
    virtual double evaluate(double time, double rest_angle, double angle, double vel, const ChLinkRSDA&) override {
        return m_cb(time, rest_angle, angle, vel, m_user);
    }

  private:
    chn_rsda_torque_cb m_cb;
    void* m_user;
};

class ContactCollector : public ChContactContainer::ReportContactCallback {
  public:
    ContactCollector(chn_contact* out, int32_t capacity) : m_out(out), m_capacity(capacity) {}
    virtual bool OnReportContact(const ChVector3d& pA,
                                 const ChVector3d& pB,
                                 const ChMatrix33<>& plane_coord,
                                 double distance,
                                 double,
                                 const ChVector3d& react_forces,
                                 const ChVector3d&,
                                 ChContactable*,
                                 ChContactable*,
                                 int) override {
        if (m_out && m_count < m_capacity) {
            chn_contact& c = m_out[m_count];
            c.point_a = to_c(pA);
            c.point_b = to_c(pB);
            c.normal = to_c(plane_coord.GetAxisX());
            c.force = to_c(plane_coord * react_forces);
            c.distance = distance;
        }
        m_count++;
        return true;
    }
    int32_t count() const { return m_count; }

  private:
    chn_contact* m_out;
    int32_t m_capacity;
    int32_t m_count = 0;
};

std::shared_ptr<ChContactMaterial> mat_or_default(chn_material* mat, ChContactMethod method) {
    if (mat)
        return mat->ptr;
    return ChContactMaterial::DefaultMaterial(method);
}

ChContactMethod method_of(chn_material* mat) {
    return mat ? mat->ptr->GetContactMethod() : ChContactMethod::NSC;
}

void fill_state(ChBody& b, chn_body_state* s) {
    s->pos = to_c(b.GetPos());
    s->rot = to_c(b.GetRot());
    s->lin_vel = to_c(b.GetPosDt());
    s->ang_vel = to_c(b.GetAngVelParent());
    ChFramed vis = b.GetVisualModelFrame();
    s->vis_pos = to_c(vis.GetPos());
    s->vis_rot = to_c(vis.GetRot());
}

std::shared_ptr<ChTriangleMeshConnected> make_trimesh(const chn_vec3* v, int32_t nv, const int32_t* idx, int32_t nt) {
    auto mesh = chrono_types::make_shared<ChTriangleMeshConnected>();
    auto& verts = mesh->GetCoordsVertices();
    auto& faces = mesh->GetIndicesVertexes();
    verts.reserve(nv);
    faces.reserve(nt);
    for (int32_t i = 0; i < nv; i++)
        verts.emplace_back(v[i].x, v[i].y, v[i].z);
    for (int32_t i = 0; i < nt; i++)
        faces.emplace_back(idx[3 * i], idx[3 * i + 1], idx[3 * i + 2]);
    return mesh;
}

chn_body* wrap_body(std::shared_ptr<ChBody> b) {
    auto* h = new chn_body_t();
    h->ptr = b;
    return h;
}

chn_link* wrap_link(std::shared_ptr<ChLinkBase> l) {
    auto* h = new chn_link_t();
    h->ptr = l;
    return h;
}

template <typename T>
void add_visual(chn_body* body, std::shared_ptr<T> shape, const chn_frame& frame, float r, float g, float b) {
    shape->SetColor(ChColor(r, g, b));
    body->ptr->AddVisualShape(shape, to_ch(frame));
}

}  // namespace

// ------------------------------------------------------------------------------------------------ library

extern "C" {

int32_t chn_abi_version(void) {
    return CHN_ABI_VERSION;
}

const char* chn_chrono_version(void) {
    return CHRONO_VERSION;
}

int32_t chn_last_error(char* buffer, int32_t capacity) {
    auto len = (int32_t)g_last_error.size();
    if (buffer && capacity > 0) {
        int32_t n = std::min(len, capacity - 1);
        std::memcpy(buffer, g_last_error.data(), n);
        buffer[n] = 0;
    }
    return len;
}

void chn_set_data_path(const char* path) {
    CHN_GUARD_BEGIN
    SetChronoDataPath(path);
    CHN_GUARD_END_VOID
}

void chn_set_default_envelope(double envelope, double margin) {
    ChCollisionModel::SetDefaultSuggestedEnvelope(envelope);
    ChCollisionModel::SetDefaultSuggestedMargin(margin);
}

// ------------------------------------------------------------------------------------------------ system

chn_system* chn_system_create(int32_t contact_method) {
    CHN_GUARD_BEGIN
    auto* h = new chn_system_t();
    if (contact_method == CHN_CONTACT_SMC)
        h->ptr = chrono_types::make_shared<ChSystemSMC>();
    else
        h->ptr = chrono_types::make_shared<ChSystemNSC>();
    h->ptr->SetCollisionSystemType(ChCollisionSystem::Type::BULLET);
    return h;
    CHN_GUARD_END(nullptr)
}

void chn_system_release(chn_system* sys) {
    delete sys;
}

int32_t chn_system_contact_method(chn_system* sys) {
    return sys->ptr->GetContactMethod() == ChContactMethod::SMC ? CHN_CONTACT_SMC : CHN_CONTACT_NSC;
}

void chn_system_set_gravity(chn_system* sys, chn_vec3 g) {
    sys->ptr->SetGravitationalAcceleration(to_ch(g));
}

chn_vec3 chn_system_get_gravity(chn_system* sys) {
    return to_c(sys->ptr->GetGravitationalAcceleration());
}

int32_t chn_system_set_solver(chn_system* sys, int32_t solver_type, int32_t max_iterations, double tolerance) {
    CHN_GUARD_BEGIN
    sys->ptr->SetSolverType(static_cast<ChSolver::Type>(solver_type));
    if (auto it = sys->ptr->GetSolver()->AsIterative()) {
        if (max_iterations > 0)
            it->SetMaxIterations(max_iterations);
        if (tolerance > 0)
            it->SetTolerance(tolerance);
    }
    return 0;
    CHN_GUARD_END(-1)
}

int32_t chn_system_set_timestepper(chn_system* sys, int32_t stepper_type) {
    CHN_GUARD_BEGIN
    sys->ptr->SetTimestepperType(static_cast<ChTimestepper::Type>(stepper_type));
    return 0;
    CHN_GUARD_END(-1)
}

int32_t chn_system_set_collision_system(chn_system* sys, int32_t type) {
    CHN_GUARD_BEGIN
    sys->ptr->SetCollisionSystemType(static_cast<ChCollisionSystem::Type>(type));
    return 0;
    CHN_GUARD_END(-1)
}

void chn_system_set_num_threads(chn_system* sys, int32_t chrono, int32_t collision, int32_t eigen) {
    sys->ptr->SetNumThreads(chrono, collision, eigen);
}

void chn_system_set_max_penetration_recovery_speed(chn_system* sys, double v) {
    sys->ptr->SetMaxPenetrationRecoverySpeed(v);
}

int32_t chn_system_step(chn_system* sys, double dt) {
    CHN_GUARD_BEGIN
    return sys->ptr->DoStepDynamics(dt);
    CHN_GUARD_END(-1)
}

int32_t chn_system_step_many(chn_system* sys, double dt, int32_t count) {
    CHN_GUARD_BEGIN
    for (int32_t i = 0; i < count; i++)
        sys->ptr->DoStepDynamics(dt);
    return 0;
    CHN_GUARD_END(-1)
}

int32_t chn_system_do_static_linear(chn_system* sys) {
    CHN_GUARD_BEGIN
    return sys->ptr->DoStaticLinear() ? 0 : 1;
    CHN_GUARD_END(-1)
}

int32_t chn_system_do_static_nonlinear(chn_system* sys, int32_t steps) {
    CHN_GUARD_BEGIN
    return sys->ptr->DoStaticNonlinear(steps) ? 0 : 1;
    CHN_GUARD_END(-1)
}

int32_t chn_system_do_assembly(chn_system* sys) {
    CHN_GUARD_BEGIN
    auto flag = sys->ptr->DoAssembly(AssemblyAnalysis::Level::FULL);
    return flag == AssemblyAnalysis::ExitFlag::NOT_CONVERGED ? 1 : 0;
    CHN_GUARD_END(-1)
}

double chn_system_get_time(chn_system* sys) {
    return sys->ptr->GetChTime();
}

void chn_system_set_time(chn_system* sys, double t) {
    sys->ptr->SetChTime(t);
}

double chn_system_get_step_timer(chn_system* sys) {
    return sys->ptr->GetTimerStep();
}

int32_t chn_system_num_contacts(chn_system* sys) {
    return (int32_t)sys->ptr->GetNumContacts();
}

int32_t chn_system_num_bodies(chn_system* sys) {
    return (int32_t)sys->ptr->GetBodies().size();
}

int32_t chn_system_num_links(chn_system* sys) {
    return (int32_t)sys->ptr->GetLinks().size();
}

int32_t chn_system_add_body(chn_system* sys, chn_body* body) {
    CHN_GUARD_BEGIN
    sys->ptr->AddBody(body->ptr);
    return 0;
    CHN_GUARD_END(-1)
}

int32_t chn_system_remove_body(chn_system* sys, chn_body* body) {
    CHN_GUARD_BEGIN
    sys->ptr->RemoveBody(body->ptr);
    return 0;
    CHN_GUARD_END(-1)
}

int32_t chn_system_add_link(chn_system* sys, chn_link* link) {
    CHN_GUARD_BEGIN
    sys->ptr->AddLink(link->ptr);
    return 0;
    CHN_GUARD_END(-1)
}

int32_t chn_system_remove_link(chn_system* sys, chn_link* link) {
    CHN_GUARD_BEGIN
    sys->ptr->RemoveLink(link->ptr);
    return 0;
    CHN_GUARD_END(-1)
}

int32_t chn_system_add_mesh(chn_system* sys, chn_mesh* mesh) {
    CHN_GUARD_BEGIN
    sys->ptr->AddMesh(mesh->ptr);
    return 0;
    CHN_GUARD_END(-1)
}

chn_body* chn_system_get_body(chn_system* sys, int32_t index) {
    const auto& bodies = sys->ptr->GetBodies();
    CHN_REQUIRE(index >= 0 && index < (int32_t)bodies.size(), "body index out of range", nullptr);
    return wrap_body(bodies[index]);
}

int32_t chn_system_get_body_states(chn_system* sys, chn_body_state* out, int32_t capacity) {
    const auto& bodies = sys->ptr->GetBodies();
    auto n = (int32_t)bodies.size();
    int32_t m = std::min(n, capacity);
    for (int32_t i = 0; i < m; i++)
        fill_state(*bodies[i], &out[i]);
    return n;
}

int32_t chn_system_get_contacts(chn_system* sys, chn_contact* out, int32_t capacity) {
    CHN_GUARD_BEGIN
    auto collector = chrono_types::make_shared<ContactCollector>(out, capacity);
    sys->ptr->GetContactContainer()->ReportAllContacts(collector);
    return collector->count();
    CHN_GUARD_END(-1)
}

int32_t chn_system_state_size(chn_system* sys, int32_t* npos, int32_t* nvel) {
    CHN_GUARD_BEGIN
    sys->ptr->Setup();
    *npos = (int32_t)sys->ptr->GetNumCoordsPosLevel();
    *nvel = (int32_t)sys->ptr->GetNumCoordsVelLevel();
    return 0;
    CHN_GUARD_END(-1)
}

int32_t chn_system_state_get(chn_system* sys, double* pos, int32_t npos, double* vel, int32_t nvel, double* time) {
    CHN_GUARD_BEGIN
    auto& s = *sys->ptr;
    s.Setup();
    CHN_REQUIRE((int32_t)s.GetNumCoordsPosLevel() == npos && (int32_t)s.GetNumCoordsVelLevel() == nvel,
                "state size mismatch", -1);
    ChState x(npos, &s);
    ChStateDelta v(nvel, &s);
    double T;
    s.StateGather(x, v, T);
    std::memcpy(pos, x.data(), sizeof(double) * npos);
    std::memcpy(vel, v.data(), sizeof(double) * nvel);
    if (time)
        *time = T;
    return 0;
    CHN_GUARD_END(-1)
}

int32_t chn_system_state_set(chn_system* sys, const double* pos, int32_t npos, const double* vel, int32_t nvel, double time) {
    CHN_GUARD_BEGIN
    auto& s = *sys->ptr;
    s.Setup();
    CHN_REQUIRE((int32_t)s.GetNumCoordsPosLevel() == npos && (int32_t)s.GetNumCoordsVelLevel() == nvel,
                "state size mismatch", -1);
    ChState x(npos, &s);
    ChStateDelta v(nvel, &s);
    std::memcpy(x.data(), pos, sizeof(double) * npos);
    std::memcpy(v.data(), vel, sizeof(double) * nvel);
    s.StateScatter(x, v, time, UpdateFlags::UPDATE_ALL);
    s.ForceUpdate();
    return 0;
    CHN_GUARD_END(-1)
}

// ------------------------------------------------------------------------------------------------ materials

chn_material* chn_material_create(int32_t contact_method) {
    CHN_GUARD_BEGIN
    auto* h = new chn_material_t();
    if (contact_method == CHN_CONTACT_SMC)
        h->ptr = chrono_types::make_shared<ChContactMaterialSMC>();
    else
        h->ptr = chrono_types::make_shared<ChContactMaterialNSC>();
    return h;
    CHN_GUARD_END(nullptr)
}

void chn_material_release(chn_material* mat) {
    delete mat;
}

void chn_material_set_friction(chn_material* mat, float v) {
    mat->ptr->SetFriction(v);
}
void chn_material_set_restitution(chn_material* mat, float v) {
    mat->ptr->SetRestitution(v);
}
void chn_material_set_rolling_friction(chn_material* mat, float v) {
    mat->ptr->SetRollingFriction(v);
}
void chn_material_set_spinning_friction(chn_material* mat, float v) {
    mat->ptr->SetSpinningFriction(v);
}
void chn_material_set_compliance(chn_material* mat, float v) {
    if (auto m = std::dynamic_pointer_cast<ChContactMaterialNSC>(mat->ptr))
        m->SetCompliance(v);
}
void chn_material_set_cohesion(chn_material* mat, float v) {
    if (auto m = std::dynamic_pointer_cast<ChContactMaterialNSC>(mat->ptr))
        m->SetCohesion(v);
}
void chn_material_set_young_modulus(chn_material* mat, float v) {
    if (auto m = std::dynamic_pointer_cast<ChContactMaterialSMC>(mat->ptr))
        m->SetYoungModulus(v);
}
void chn_material_set_poisson_ratio(chn_material* mat, float v) {
    if (auto m = std::dynamic_pointer_cast<ChContactMaterialSMC>(mat->ptr))
        m->SetPoissonRatio(v);
}
void chn_material_set_adhesion(chn_material* mat, float v) {
    if (auto m = std::dynamic_pointer_cast<ChContactMaterialSMC>(mat->ptr))
        m->SetAdhesion(v);
}

// ------------------------------------------------------------------------------------------------ bodies

chn_body* chn_body_create(void) {
    CHN_GUARD_BEGIN
    return wrap_body(chrono_types::make_shared<ChBody>());
    CHN_GUARD_END(nullptr)
}

chn_body* chn_body_create_box(chn_vec3 size, double density, int32_t visual, int32_t collide, chn_material* mat) {
    CHN_GUARD_BEGIN
    return wrap_body(chrono_types::make_shared<ChBodyEasyBox>(size.x, size.y, size.z, density, visual != 0,
                                                              collide != 0, mat ? mat->ptr : nullptr));
    CHN_GUARD_END(nullptr)
}

chn_body* chn_body_create_sphere(double radius, double density, int32_t visual, int32_t collide, chn_material* mat) {
    CHN_GUARD_BEGIN
    return wrap_body(chrono_types::make_shared<ChBodyEasySphere>(radius, density, visual != 0, collide != 0,
                                                                 mat ? mat->ptr : nullptr));
    CHN_GUARD_END(nullptr)
}

chn_body* chn_body_create_cylinder(int32_t axis, double radius, double height, double density, int32_t visual,
                                   int32_t collide, chn_material* mat) {
    CHN_GUARD_BEGIN
    ChAxis ax = axis == 0 ? ChAxis::X : (axis == 2 ? ChAxis::Z : ChAxis::Y);
    return wrap_body(chrono_types::make_shared<ChBodyEasyCylinder>(ax, radius, height, density, visual != 0,
                                                                   collide != 0, mat ? mat->ptr : nullptr));
    CHN_GUARD_END(nullptr)
}

chn_body* chn_body_create_ellipsoid(chn_vec3 axes, double density, int32_t visual, int32_t collide, chn_material* mat) {
    CHN_GUARD_BEGIN
    // ChBodyEasyEllipsoid takes full axis lengths.
    ChVector3d full(2 * axes.x, 2 * axes.y, 2 * axes.z);
    return wrap_body(chrono_types::make_shared<ChBodyEasyEllipsoid>(full, density, visual != 0, collide != 0,
                                                                    mat ? mat->ptr : nullptr));
    CHN_GUARD_END(nullptr)
}

chn_body* chn_body_create_convex_hull(const chn_vec3* points, int32_t count, double density, int32_t visual,
                                      int32_t collide, chn_material* mat) {
    CHN_GUARD_BEGIN
    std::vector<ChVector3d> pts;
    pts.reserve(count);
    for (int32_t i = 0; i < count; i++)
        pts.emplace_back(points[i].x, points[i].y, points[i].z);
    return wrap_body(chrono_types::make_shared<ChBodyEasyConvexHull>(pts, density, visual != 0, collide != 0,
                                                                     mat ? mat->ptr : nullptr));
    CHN_GUARD_END(nullptr)
}

chn_body* chn_body_create_mesh(const chn_vec3* vertices, int32_t nvert, const int32_t* indices, int32_t ntri,
                               double density, int32_t compute_mass, int32_t visual, int32_t collide,
                               chn_material* mat, double sphere_swept) {
    CHN_GUARD_BEGIN
    auto mesh = make_trimesh(vertices, nvert, indices, ntri);
    return wrap_body(chrono_types::make_shared<ChBodyEasyMesh>(mesh, density, compute_mass != 0, visual != 0,
                                                               collide != 0, mat ? mat->ptr : nullptr, sphere_swept));
    CHN_GUARD_END(nullptr)
}

void chn_body_release(chn_body* body) {
    delete body;
}

void chn_body_set_name(chn_body* body, const char* name) {
    body->ptr->SetName(name ? name : "");
}

int32_t chn_body_get_name(chn_body* body, char* buffer, int32_t capacity) {
    const std::string& n = body->ptr->GetName();
    auto len = (int32_t)n.size();
    if (buffer && capacity > 0) {
        int32_t m = std::min(len, capacity - 1);
        std::memcpy(buffer, n.data(), m);
        buffer[m] = 0;
    }
    return len;
}

void chn_body_set_pos(chn_body* body, chn_vec3 p) {
    body->ptr->SetPos(to_ch(p));
}
chn_vec3 chn_body_get_pos(chn_body* body) {
    return to_c(body->ptr->GetPos());
}
void chn_body_set_rot(chn_body* body, chn_quat q) {
    body->ptr->SetRot(to_ch(q));
}
chn_quat chn_body_get_rot(chn_body* body) {
    return to_c(body->ptr->GetRot());
}
void chn_body_set_lin_vel(chn_body* body, chn_vec3 v) {
    body->ptr->SetPosDt(to_ch(v));
}
chn_vec3 chn_body_get_lin_vel(chn_body* body) {
    return to_c(body->ptr->GetPosDt());
}
void chn_body_set_ang_vel(chn_body* body, chn_vec3 w) {
    body->ptr->SetAngVelParent(to_ch(w));
}
chn_vec3 chn_body_get_ang_vel(chn_body* body) {
    return to_c(body->ptr->GetAngVelParent());
}
chn_vec3 chn_body_get_lin_acc(chn_body* body) {
    return to_c(body->ptr->GetPosDt2());
}
void chn_body_get_state(chn_body* body, chn_body_state* out) {
    fill_state(*body->ptr, out);
}
void chn_body_set_mass(chn_body* body, double mass) {
    body->ptr->SetMass(mass);
}
double chn_body_get_mass(chn_body* body) {
    return body->ptr->GetMass();
}
void chn_body_set_inertia_xx(chn_body* body, chn_vec3 ixx) {
    body->ptr->SetInertiaXX(to_ch(ixx));
}
chn_vec3 chn_body_get_inertia_xx(chn_body* body) {
    return to_c(body->ptr->GetInertiaXX());
}
void chn_body_set_inertia_xy(chn_body* body, chn_vec3 ixy) {
    body->ptr->SetInertiaXY(to_ch(ixy));
}
void chn_body_set_fixed(chn_body* body, int32_t fixed) {
    body->ptr->SetFixed(fixed != 0);
}
int32_t chn_body_is_fixed(chn_body* body) {
    return body->ptr->IsFixed() ? 1 : 0;
}
void chn_body_enable_collision(chn_body* body, int32_t enable) {
    body->ptr->EnableCollision(enable != 0);
}
int32_t chn_body_collision_enabled(chn_body* body) {
    return body->ptr->IsCollisionEnabled() ? 1 : 0;
}
void chn_body_set_sleeping_allowed(chn_body* body, int32_t allowed) {
    body->ptr->SetSleepingAllowed(allowed != 0);
}
chn_vec3 chn_body_get_contact_force(chn_body* body) {
    return to_c(body->ptr->GetContactForce());
}
chn_vec3 chn_body_get_contact_torque(chn_body* body) {
    return to_c(body->ptr->GetContactTorque());
}

void chn_body_clear_forces(chn_body* body) {
    body->ptr->EmptyAccumulator(body->acc());
}
void chn_body_add_force(chn_body* body, chn_vec3 force, chn_vec3 point, int32_t local) {
    body->ptr->AccumulateForce(body->acc(), to_ch(force), to_ch(point), local != 0);
}
void chn_body_add_torque(chn_body* body, chn_vec3 torque, int32_t local) {
    body->ptr->AccumulateTorque(body->acc(), to_ch(torque), local != 0);
}

#define CHN_ADD_COLLISION(expr)                                   \
    CHN_GUARD_BEGIN                                               \
    auto m = mat_or_default(mat, method_of(mat));                 \
    body->ptr->AddCollisionShape(expr, to_ch(frame));             \
    return 0;                                                     \
    CHN_GUARD_END(-1)

int32_t chn_body_add_collision_box(chn_body* body, chn_material* mat, chn_vec3 size, chn_frame frame) {
    CHN_ADD_COLLISION(chrono_types::make_shared<ChCollisionShapeBox>(m, size.x, size.y, size.z))
}
int32_t chn_body_add_collision_sphere(chn_body* body, chn_material* mat, double radius, chn_frame frame) {
    CHN_ADD_COLLISION(chrono_types::make_shared<ChCollisionShapeSphere>(m, radius))
}
int32_t chn_body_add_collision_cylinder(chn_body* body, chn_material* mat, double radius, double height, chn_frame frame) {
    CHN_ADD_COLLISION(chrono_types::make_shared<ChCollisionShapeCylinder>(m, radius, height))
}
int32_t chn_body_add_collision_capsule(chn_body* body, chn_material* mat, double radius, double height, chn_frame frame) {
    CHN_ADD_COLLISION(chrono_types::make_shared<ChCollisionShapeCapsule>(m, radius, height))
}
int32_t chn_body_add_collision_cone(chn_body* body, chn_material* mat, double radius, double height, chn_frame frame) {
    CHN_ADD_COLLISION(chrono_types::make_shared<ChCollisionShapeCone>(m, radius, height))
}
int32_t chn_body_add_collision_mesh(chn_body* body, chn_material* mat, const chn_vec3* vertices, int32_t nvert,
                                    const int32_t* indices, int32_t ntri, int32_t is_static, int32_t is_convex,
                                    double sphere_swept, chn_frame frame) {
    CHN_ADD_COLLISION(chrono_types::make_shared<ChCollisionShapeTriangleMesh>(
        m, make_trimesh(vertices, nvert, indices, ntri), is_static != 0, is_convex != 0, sphere_swept))
}

int32_t chn_body_add_visual_box(chn_body* body, chn_vec3 size, chn_frame frame, float r, float g, float b) {
    CHN_GUARD_BEGIN
    add_visual(body, chrono_types::make_shared<ChVisualShapeBox>(size.x, size.y, size.z), frame, r, g, b);
    return 0;
    CHN_GUARD_END(-1)
}
int32_t chn_body_add_visual_sphere(chn_body* body, double radius, chn_frame frame, float r, float g, float b) {
    CHN_GUARD_BEGIN
    add_visual(body, chrono_types::make_shared<ChVisualShapeSphere>(radius), frame, r, g, b);
    return 0;
    CHN_GUARD_END(-1)
}
int32_t chn_body_add_visual_cylinder(chn_body* body, double radius, double height, chn_frame frame, float r, float g, float b) {
    CHN_GUARD_BEGIN
    add_visual(body, chrono_types::make_shared<ChVisualShapeCylinder>(radius, height), frame, r, g, b);
    return 0;
    CHN_GUARD_END(-1)
}
int32_t chn_body_add_visual_capsule(chn_body* body, double radius, double height, chn_frame frame, float r, float g, float b) {
    CHN_GUARD_BEGIN
    add_visual(body, chrono_types::make_shared<ChVisualShapeCapsule>(radius, height), frame, r, g, b);
    return 0;
    CHN_GUARD_END(-1)
}
int32_t chn_body_add_visual_cone(chn_body* body, double radius, double height, chn_frame frame, float r, float g, float b) {
    CHN_GUARD_BEGIN
    add_visual(body, chrono_types::make_shared<ChVisualShapeCone>(radius, height), frame, r, g, b);
    return 0;
    CHN_GUARD_END(-1)
}

int32_t chn_body_set_color(chn_body* body, float r, float g, float b) {
    CHN_GUARD_BEGIN
    auto model = body->ptr->GetVisualModel();
    if (!model)
        return 0;
    for (auto& inst : model->GetShapeInstances())
        inst.shape->SetColor(ChColor(r, g, b));
    return 0;
    CHN_GUARD_END(-1)
}

int32_t chn_body_get_visual_shapes(chn_body* body, chn_visual_shape* out, int32_t capacity) {
    CHN_GUARD_BEGIN
    auto model = body->ptr->GetVisualModel();
    if (!model)
        return 0;
    const auto& shapes = model->GetShapeInstances();
    auto n = (int32_t)shapes.size();
    for (int32_t i = 0; i < n && i < capacity; i++) {
        const auto& inst = shapes[i];
        chn_visual_shape& s = out[i];
        s.index = i;
        s.frame = to_c(inst.frame);
        s.dims = {0, 0, 0};
        s.type = CHN_SHAPE_OTHER;
        ChVisualShape* raw = inst.shape.get();
        if (auto box = dynamic_cast<ChVisualShapeBox*>(raw)) {
            s.type = CHN_SHAPE_BOX;
            s.dims = to_c(box->GetLengths());
        } else if (auto sph = dynamic_cast<ChVisualShapeSphere*>(raw)) {
            s.type = CHN_SHAPE_SPHERE;
            s.dims = {sph->GetRadius(), 0, 0};
        } else if (auto cyl = dynamic_cast<ChVisualShapeCylinder*>(raw)) {
            s.type = CHN_SHAPE_CYLINDER;
            s.dims = {cyl->GetRadius(), cyl->GetHeight(), 0};
        } else if (auto cap = dynamic_cast<ChVisualShapeCapsule*>(raw)) {
            s.type = CHN_SHAPE_CAPSULE;
            s.dims = {cap->GetRadius(), cap->GetHeight(), 0};
        } else if (auto cone = dynamic_cast<ChVisualShapeCone*>(raw)) {
            s.type = CHN_SHAPE_CONE;
            s.dims = {cone->GetRadius(), cone->GetHeight(), 0};
        } else if (auto ell = dynamic_cast<ChVisualShapeEllipsoid*>(raw)) {
            s.type = CHN_SHAPE_ELLIPSOID;
            s.dims = to_c(ell->GetSemiaxes());
        } else if (dynamic_cast<ChVisualShapeTriangleMesh*>(raw)) {
            s.type = CHN_SHAPE_MESH;
        }
        ChColor c = inst.shape->GetColor();
        s.r = c.R;
        s.g = c.G;
        s.b = c.B;
        s.a = 1.0f;
    }
    return n;
    CHN_GUARD_END(-1)
}

int32_t chn_body_get_visual_mesh(chn_body* body, int32_t shape_index, chn_vec3* vertices, int32_t* nvert,
                                 int32_t* indices, int32_t* ntri) {
    CHN_GUARD_BEGIN
    auto model = body->ptr->GetVisualModel();
    CHN_REQUIRE(model, "body has no visual model", -1);
    const auto& shapes = model->GetShapeInstances();
    CHN_REQUIRE(shape_index >= 0 && shape_index < (int32_t)shapes.size(), "shape index out of range", -1);
    auto tm = std::dynamic_pointer_cast<ChVisualShapeTriangleMesh>(shapes[shape_index].shape);
    CHN_REQUIRE(tm, "shape is not a triangle mesh", -1);
    auto mesh = tm->GetMesh();
    const auto& v = mesh->GetCoordsVertices();
    const auto& f = mesh->GetIndicesVertexes();
    if (vertices && indices) {
        CHN_REQUIRE(*nvert >= (int32_t)v.size() && *ntri >= (int32_t)f.size(), "buffer too small", -1);
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

// ------------------------------------------------------------------------------------------------ functions

static chn_function* wrap_fn(std::shared_ptr<ChFunction> f) {
    auto* h = new chn_function_t();
    h->ptr = f;
    return h;
}

chn_function* chn_function_const(double value) {
    return wrap_fn(chrono_types::make_shared<ChFunctionConst>(value));
}
chn_function* chn_function_ramp(double y0, double slope) {
    return wrap_fn(chrono_types::make_shared<ChFunctionRamp>(y0, slope));
}
chn_function* chn_function_sine(double amplitude, double frequency, double phase, double shift) {
    return wrap_fn(chrono_types::make_shared<ChFunctionSine>(amplitude, frequency, phase, shift));
}
chn_function* chn_function_poly345(double height, double width) {
    return wrap_fn(chrono_types::make_shared<ChFunctionPoly345>(height, width));
}
chn_function* chn_function_setpoint(void) {
    return wrap_fn(chrono_types::make_shared<ChFunctionSetpoint>());
}
void chn_function_setpoint_set(chn_function* fn, double value) {
    if (auto sp = std::dynamic_pointer_cast<ChFunctionSetpoint>(fn->ptr))
        sp->SetSetpointAndDerivatives(value, 0, 0);
}
chn_function* chn_function_callback(chn_function_cb cb, void* user) {
    CHN_REQUIRE(cb, "callback is null", nullptr);
    return wrap_fn(chrono_types::make_shared<CallbackFunction>(cb, user));
}
double chn_function_eval(chn_function* fn, double x) {
    return fn->ptr->GetVal(x);
}
void chn_function_release(chn_function* fn) {
    delete fn;
}

// ------------------------------------------------------------------------------------------------ links

chn_link* chn_link_create_joint(int32_t kind, chn_body* body1, chn_body* body2, chn_frame frame) {
    CHN_GUARD_BEGIN
    CHN_REQUIRE(body1 && body2, "both bodies are required", nullptr);
    ChFramed f = to_ch(frame);
    auto b1 = body1->ptr;
    auto b2 = body2->ptr;
    std::shared_ptr<ChLinkBase> link;
    auto lock = [&](auto l) {
        l->Initialize(b1, b2, f);
        link = l;
    };
    switch (kind) {
        case CHN_LINK_LOCK: lock(chrono_types::make_shared<ChLinkLockLock>()); break;
        case CHN_LINK_REVOLUTE: lock(chrono_types::make_shared<ChLinkLockRevolute>()); break;
        case CHN_LINK_SPHERICAL: lock(chrono_types::make_shared<ChLinkLockSpherical>()); break;
        case CHN_LINK_CYLINDRICAL: lock(chrono_types::make_shared<ChLinkLockCylindrical>()); break;
        case CHN_LINK_PRISMATIC: lock(chrono_types::make_shared<ChLinkLockPrismatic>()); break;
        case CHN_LINK_POINT_PLANE: lock(chrono_types::make_shared<ChLinkLockPointPlane>()); break;
        case CHN_LINK_POINT_LINE: lock(chrono_types::make_shared<ChLinkLockPointLine>()); break;
        case CHN_LINK_PLANAR: lock(chrono_types::make_shared<ChLinkLockPlanar>()); break;
        case CHN_LINK_OLDHAM: lock(chrono_types::make_shared<ChLinkLockOldham>()); break;
        case CHN_LINK_UNIVERSAL: {
            auto l = chrono_types::make_shared<ChLinkUniversal>();
            l->Initialize(b1, b2, f);
            link = l;
            break;
        }
        case CHN_LINK_PARALLEL: {
            auto l = chrono_types::make_shared<ChLinkMateParallel>();
            l->Initialize(b1, b2, f);
            link = l;
            break;
        }
        case CHN_LINK_ORTHOGONAL: {
            auto l = chrono_types::make_shared<ChLinkMateOrthogonal>();
            l->Initialize(b1, b2, f);
            link = l;
            break;
        }
        default: set_error("unknown joint kind"); return nullptr;
    }
    return wrap_link(link);
    CHN_GUARD_END(nullptr)
}

chn_link* chn_link_create_distance(chn_body* body1, chn_body* body2, chn_vec3 p1, chn_vec3 p2, int32_t local) {
    CHN_GUARD_BEGIN
    auto l = chrono_types::make_shared<ChLinkDistance>();
    l->Initialize(body1->ptr, body2->ptr, local != 0, to_ch(p1), to_ch(p2));
    return wrap_link(l);
    CHN_GUARD_END(nullptr)
}

chn_link* chn_link_create_gear(chn_body* body1, chn_body* body2, double ratio, int32_t epicyclic, chn_frame shaft1,
                               chn_frame shaft2) {
    CHN_GUARD_BEGIN
    auto l = chrono_types::make_shared<ChLinkLockGear>();
    l->Initialize(body1->ptr, body2->ptr, ChFramed());
    l->SetFrameShaft1(to_ch(shaft1));
    l->SetFrameShaft2(to_ch(shaft2));
    l->SetTransmissionRatio(ratio);
    l->SetEpicyclic(epicyclic != 0);
    return wrap_link(l);
    CHN_GUARD_END(nullptr)
}

chn_link* chn_link_create_rack_pinion(chn_body* pinion, chn_body* rack, double pinion_radius, chn_frame pinion_frame,
                                      chn_frame rack_frame) {
    CHN_GUARD_BEGIN
    auto l = chrono_types::make_shared<ChLinkMateRackPinion>();
    l->Initialize(pinion->ptr, rack->ptr, false, ChFramed(), ChFramed());
    l->SetPinionRadius(pinion_radius);
    l->SetPinionFrame(to_ch(pinion_frame));
    l->SetRackFrame(to_ch(rack_frame));
    return wrap_link(l);
    CHN_GUARD_END(nullptr)
}

chn_link* chn_link_create_motor(int32_t kind, chn_body* body1, chn_body* body2, chn_frame frame, chn_function* fn) {
    CHN_GUARD_BEGIN
    std::shared_ptr<ChLinkMotor> m;
    switch (kind) {
        case CHN_MOTOR_ROT_ANGLE: m = chrono_types::make_shared<ChLinkMotorRotationAngle>(); break;
        case CHN_MOTOR_ROT_SPEED: m = chrono_types::make_shared<ChLinkMotorRotationSpeed>(); break;
        case CHN_MOTOR_ROT_TORQUE: m = chrono_types::make_shared<ChLinkMotorRotationTorque>(); break;
        case CHN_MOTOR_LIN_POSITION: m = chrono_types::make_shared<ChLinkMotorLinearPosition>(); break;
        case CHN_MOTOR_LIN_SPEED: m = chrono_types::make_shared<ChLinkMotorLinearSpeed>(); break;
        case CHN_MOTOR_LIN_FORCE: m = chrono_types::make_shared<ChLinkMotorLinearForce>(); break;
        default: set_error("unknown motor kind"); return nullptr;
    }
    m->Initialize(body1->ptr, body2->ptr, to_ch(frame));
    if (fn)
        m->SetMotorFunction(fn->ptr);
    return wrap_link(m);
    CHN_GUARD_END(nullptr)
}

int32_t chn_motor_set_function(chn_link* link, chn_function* fn) {
    auto m = std::dynamic_pointer_cast<ChLinkMotor>(link->ptr);
    CHN_REQUIRE(m, "link is not a motor", -1);
    m->SetMotorFunction(fn->ptr);
    return 0;
}

int32_t chn_motor_get(chn_link* link, double* value, double* speed, double* effort) {
    CHN_GUARD_BEGIN
    if (auto r = std::dynamic_pointer_cast<ChLinkMotorRotation>(link->ptr)) {
        *value = r->GetMotorAngle();
        *speed = r->GetMotorAngleDt();
        *effort = r->GetMotorTorque();
        return 0;
    }
    if (auto l = std::dynamic_pointer_cast<ChLinkMotorLinear>(link->ptr)) {
        *value = l->GetMotorPos();
        *speed = l->GetMotorPosDt();
        *effort = l->GetMotorForce();
        return 0;
    }
    set_error("link is not a motor");
    return -1;
    CHN_GUARD_END(-1)
}

chn_link* chn_link_create_spring(chn_body* body1, chn_body* body2, chn_vec3 p1, chn_vec3 p2, int32_t local, double k,
                                 double c, double rest_length, double actuator_force) {
    CHN_GUARD_BEGIN
    auto l = chrono_types::make_shared<ChLinkTSDA>();
    l->Initialize(body1->ptr, body2->ptr, local != 0, to_ch(p1), to_ch(p2));
    l->SetSpringCoefficient(k);
    l->SetDampingCoefficient(c);
    l->SetActuatorForce(actuator_force);
    if (rest_length >= 0)
        l->SetRestLength(rest_length);
    return wrap_link(l);
    CHN_GUARD_END(nullptr)
}

int32_t chn_spring_set_force_callback(chn_link* link, chn_tsda_force_cb cb, void* user) {
    auto l = std::dynamic_pointer_cast<ChLinkTSDA>(link->ptr);
    CHN_REQUIRE(l, "link is not a spring", -1);
    l->RegisterForceFunctor(chrono_types::make_shared<CallbackForce>(cb, user));
    return 0;
}

int32_t chn_spring_get(chn_link* link, double* length, double* velocity, double* force) {
    auto l = std::dynamic_pointer_cast<ChLinkTSDA>(link->ptr);
    CHN_REQUIRE(l, "link is not a spring", -1);
    *length = l->GetLength();
    *velocity = l->GetVelocity();
    *force = l->GetForce();
    return 0;
}

chn_link* chn_link_create_rot_spring(chn_body* body1, chn_body* body2, chn_frame frame, double k, double c,
                                     double rest_angle, double actuator_torque) {
    CHN_GUARD_BEGIN
    auto l = chrono_types::make_shared<ChLinkRSDA>();
    l->Initialize(body1->ptr, body2->ptr, to_ch(frame));
    l->SetSpringCoefficient(k);
    l->SetDampingCoefficient(c);
    l->SetActuatorTorque(actuator_torque);
    l->SetRestAngle(rest_angle);
    return wrap_link(l);
    CHN_GUARD_END(nullptr)
}

int32_t chn_rot_spring_set_torque_callback(chn_link* link, chn_rsda_torque_cb cb, void* user) {
    auto l = std::dynamic_pointer_cast<ChLinkRSDA>(link->ptr);
    CHN_REQUIRE(l, "link is not a rotational spring", -1);
    l->RegisterTorqueFunctor(chrono_types::make_shared<CallbackTorque>(cb, user));
    return 0;
}

int32_t chn_rot_spring_get(chn_link* link, double* angle, double* velocity, double* torque) {
    auto l = std::dynamic_pointer_cast<ChLinkRSDA>(link->ptr);
    CHN_REQUIRE(l, "link is not a rotational spring", -1);
    *angle = l->GetAngle();
    *velocity = l->GetVelocity();
    *torque = l->GetTorque();
    return 0;
}

int32_t chn_link_set_limits(chn_link* link, int32_t axis, double min, double max) {
    CHN_GUARD_BEGIN
    auto l = std::dynamic_pointer_cast<ChLinkLock>(link->ptr);
    CHN_REQUIRE(l, "limits are supported on lock-family joints only", -1);
    ChLinkLimit* lim = nullptr;
    switch (axis) {
        case 0: lim = &l->LimitX(); break;
        case 1: lim = &l->LimitY(); break;
        case 2: lim = &l->LimitZ(); break;
        case 3: lim = &l->LimitRx(); break;
        case 4: lim = &l->LimitRy(); break;
        case 5: lim = &l->LimitRz(); break;
        default: set_error("axis must be 0..5"); return -1;
    }
    lim->SetActive(true);
    lim->SetMin(min);
    lim->SetMax(max);
    return 0;
    CHN_GUARD_END(-1)
}

void chn_link_release(chn_link* link) {
    delete link;
}

void chn_link_set_name(chn_link* link, const char* name) {
    link->ptr->SetName(name ? name : "");
}

int32_t chn_link_get_frames(chn_link* link, chn_frame* frame1_abs, chn_frame* frame2_abs) {
    CHN_GUARD_BEGIN
    if (frame1_abs)
        *frame1_abs = to_c(link->ptr->GetFrame1Abs());
    if (frame2_abs)
        *frame2_abs = to_c(link->ptr->GetFrame2Abs());
    return 0;
    CHN_GUARD_END(-1)
}

int32_t chn_link_get_reaction(chn_link* link, chn_vec3* force, chn_vec3* torque) {
    CHN_GUARD_BEGIN
    ChWrenchd w = link->ptr->GetReaction2();
    ChFramed f2 = link->ptr->GetFrame2Abs();
    if (force)
        *force = to_c(f2.TransformDirectionLocalToParent(w.force));
    if (torque)
        *torque = to_c(f2.TransformDirectionLocalToParent(w.torque));
    return 0;
    CHN_GUARD_END(-1)
}

void chn_link_set_disabled(chn_link* link, int32_t disabled) {
    link->ptr->SetDisabled(disabled != 0);
}

// ------------------------------------------------------------------------------------------------ FEA

chn_mesh* chn_mesh_create(void) {
    CHN_GUARD_BEGIN
    auto* h = new chn_mesh_t();
    h->ptr = chrono_types::make_shared<ChMesh>();
    return h;
    CHN_GUARD_END(nullptr)
}

void chn_mesh_release(chn_mesh* mesh) {
    delete mesh;
}

void chn_mesh_set_automatic_gravity(chn_mesh* mesh, int32_t enable) {
    mesh->ptr->SetAutomaticGravity(enable != 0);
}

int32_t chn_mesh_build_beam_euler(chn_mesh* mesh, chn_vec3 a, chn_vec3 b, chn_vec3 ydir, int32_t elements,
                                  double width_y, double width_z, double young, double shear, double density,
                                  double rayleigh_damping) {
    CHN_GUARD_BEGIN
    auto section = chrono_types::make_shared<ChBeamSectionEulerAdvanced>();
    section->SetAsRectangularSection(width_y, width_z);
    section->SetYoungModulus(young);
    section->SetShearModulus(shear);
    section->SetDensity(density);
    section->SetRayleighDamping(rayleigh_damping);
    auto first = (int32_t)mesh->ptr->GetNumNodes();
    ChBuilderBeamEuler builder;
    builder.BuildBeam(mesh->ptr, section, elements, to_ch(a), to_ch(b), to_ch(ydir));
    return first;
    CHN_GUARD_END(-1)
}

int32_t chn_mesh_build_cable_ancf(chn_mesh* mesh, chn_vec3 a, chn_vec3 b, int32_t elements, double diameter,
                                  double young, double density, double rayleigh_damping) {
    CHN_GUARD_BEGIN
    auto section = chrono_types::make_shared<ChBeamSectionCable>();
    section->SetDiameter(diameter);
    section->SetYoungModulus(young);
    section->SetDensity(density);
    section->SetRayleighDamping(rayleigh_damping);
    auto first = (int32_t)mesh->ptr->GetNumNodes();
    ChBuilderCableANCF builder;
    builder.BuildBeam(mesh->ptr, section, elements, to_ch(a), to_ch(b));
    return first;
    CHN_GUARD_END(-1)
}

int32_t chn_mesh_num_nodes(chn_mesh* mesh) {
    return (int32_t)mesh->ptr->GetNumNodes();
}

int32_t chn_mesh_num_elements(chn_mesh* mesh) {
    return (int32_t)mesh->ptr->GetNumElements();
}

// ChMesh::GetNode returns the generic ChNodeBase; FEA operations need the FEA node type.
static std::shared_ptr<ChNodeFEAbase> fea_node(const ChMesh& mesh, unsigned int i) {
    return std::dynamic_pointer_cast<ChNodeFEAbase>(mesh.GetNode(i));
}

static bool node_pos(const std::shared_ptr<ChNodeFEAbase>& node, ChVector3d& p) {
    if (auto n = std::dynamic_pointer_cast<ChNodeFEAxyz>(node)) {
        p = n->GetPos();
        return true;
    }
    if (auto n = std::dynamic_pointer_cast<ChNodeFEAxyzrot>(node)) {
        p = n->GetPos();
        return true;
    }
    return false;
}

int32_t chn_mesh_get_node_positions(chn_mesh* mesh, chn_vec3* out, int32_t capacity) {
    auto n = (int32_t)mesh->ptr->GetNumNodes();
    for (int32_t i = 0; i < n && i < capacity; i++) {
        ChVector3d p;
        out[i] = node_pos(fea_node(*mesh->ptr, i), p) ? to_c(p) : chn_vec3{0, 0, 0};
    }
    return n;
}

int32_t chn_mesh_get_line_elements(chn_mesh* mesh, int32_t* out_pairs, int32_t capacity_pairs) {
    CHN_GUARD_BEGIN
    auto& m = *mesh->ptr;
    std::unordered_map<ChNodeFEAbase*, int32_t> index;
    index.reserve(m.GetNumNodes());
    for (unsigned int i = 0; i < m.GetNumNodes(); i++)
        index[fea_node(m, i).get()] = (int32_t)i;
    int32_t count = 0;
    for (unsigned int e = 0; e < m.GetNumElements(); e++) {
        auto el = m.GetElement(e);
        if (el->GetNumNodes() != 2)
            continue;
        if (out_pairs && count < capacity_pairs) {
            out_pairs[2 * count] = index[el->GetNode(0).get()];
            out_pairs[2 * count + 1] = index[el->GetNode(1).get()];
        }
        count++;
    }
    return count;
    CHN_GUARD_END(-1)
}

int32_t chn_mesh_node_set_fixed(chn_mesh* mesh, int32_t node, int32_t fixed) {
    CHN_REQUIRE(node >= 0 && node < (int32_t)mesh->ptr->GetNumNodes(), "node index out of range", -1);
    auto n = fea_node(*mesh->ptr, node);
    CHN_REQUIRE(n, "not an FEA node", -1);
    n->SetFixed(fixed != 0);
    return 0;
}

int32_t chn_mesh_node_set_force(chn_mesh* mesh, int32_t node, chn_vec3 force) {
    CHN_REQUIRE(node >= 0 && node < (int32_t)mesh->ptr->GetNumNodes(), "node index out of range", -1);
    auto n = mesh->ptr->GetNode(node);
    if (auto a = std::dynamic_pointer_cast<ChNodeFEAxyz>(n)) {
        a->SetForce(to_ch(force));
        return 0;
    }
    if (auto r = std::dynamic_pointer_cast<ChNodeFEAxyzrot>(n)) {
        r->SetForce(to_ch(force));
        return 0;
    }
    set_error("unsupported node type");
    return -1;
}

int32_t chn_mesh_node_attach(chn_system* sys, chn_mesh* mesh, int32_t node, chn_body* body) {
    CHN_GUARD_BEGIN
    CHN_REQUIRE(node >= 0 && node < (int32_t)mesh->ptr->GetNumNodes(), "node index out of range", -1);
    auto n = mesh->ptr->GetNode(node);
    if (auto a = std::dynamic_pointer_cast<ChNodeFEAxyz>(n)) {
        auto link = chrono_types::make_shared<ChLinkNodeFrame>();
        link->Initialize(a, body->ptr);
        sys->ptr->Add(link);
        return 0;
    }
    if (auto r = std::dynamic_pointer_cast<ChNodeFEAxyzrot>(n)) {
        auto link = chrono_types::make_shared<ChLinkMateFix>();
        link->Initialize(r, body->ptr);
        sys->ptr->Add(link);
        return 0;
    }
    set_error("unsupported node type");
    return -1;
    CHN_GUARD_END(-1)
}

}  // extern "C"
