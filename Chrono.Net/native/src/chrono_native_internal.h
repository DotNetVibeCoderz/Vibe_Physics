// Internal handle types and conversions shared by the shim translation units.
#pragma once

#include <climits>
#include <memory>
#include <string>

#include "chrono_native.h"
#include "chrono/physics/ChSystemNSC.h"
#include "chrono/physics/ChSystemSMC.h"
#include "chrono/physics/ChBody.h"
#include "chrono/physics/ChLinkBase.h"
#include "chrono/physics/ChContactMaterial.h"
#include "chrono/functions/ChFunctionBase.h"
#include "chrono/fea/ChMesh.h"

struct chn_system_t {
    std::shared_ptr<chrono::ChSystem> ptr;
};
struct chn_body_t {
    std::shared_ptr<chrono::ChBody> ptr;
    unsigned int accumulator = UINT_MAX;  // created lazily on first applied load

    unsigned int acc() {
        if (accumulator == UINT_MAX)
            accumulator = ptr->AddAccumulator();
        return accumulator;
    }
};
struct chn_link_t {
    std::shared_ptr<chrono::ChLinkBase> ptr;
};
struct chn_material_t {
    std::shared_ptr<chrono::ChContactMaterial> ptr;
};
struct chn_function_t {
    std::shared_ptr<chrono::ChFunction> ptr;
};
struct chn_mesh_t {
    std::shared_ptr<chrono::fea::ChMesh> ptr;
};

namespace chrono_native {

extern thread_local std::string g_last_error;
void set_error(const char* msg);

inline chrono::ChVector3d to_ch(const chn_vec3& v) {
    return chrono::ChVector3d(v.x, v.y, v.z);
}
inline chrono::ChQuaterniond to_ch(const chn_quat& q) {
    return chrono::ChQuaterniond(q.e0, q.e1, q.e2, q.e3);
}
inline chrono::ChFramed to_ch(const chn_frame& f) {
    chrono::ChQuaterniond q = to_ch(f.rot);
    // A zero quaternion (default-initialized struct) means identity.
    if (q.Length2() < 1e-20)
        q = chrono::QUNIT;
    else
        q.Normalize();
    return chrono::ChFramed(to_ch(f.pos), q);
}
inline chn_vec3 to_c(const chrono::ChVector3d& v) {
    return {v.x(), v.y(), v.z()};
}
inline chn_quat to_c(const chrono::ChQuaterniond& q) {
    return {q.e0(), q.e1(), q.e2(), q.e3()};
}
inline chn_frame to_c(const chrono::ChFramed& f) {
    return {to_c(f.GetPos()), to_c(f.GetRot())};
}

}  // namespace chrono_native
