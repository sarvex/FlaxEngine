// Copyright (c) 2012-2023 Wojciech Figat. All rights reserved.

#include "WheeledVehicle.h"
#include "Engine/Physics/Physics.h"
#include "Engine/Level/Scene/Scene.h"
#include "Engine/Physics/PhysicsBackend.h"
#include "Engine/Physics/PhysicsScene.h"
#include "Engine/Serialization/Serialization.h"
#if USE_EDITOR
#include "Engine/Level/Scene/SceneRendering.h"
#include "Engine/Debug/DebugDraw.h"
#endif

WheeledVehicle::WheeledVehicle(const SpawnParams& params)
    : RigidBody(params)
{
    _useCCD = 1;
}

WheeledVehicle::DriveTypes WheeledVehicle::GetDriveType() const
{
    return _driveType;
}

void WheeledVehicle::SetDriveType(DriveTypes value)
{
    if (_driveType == value)
        return;
    _driveType = value;
    Setup();
}

const Array<WheeledVehicle::Wheel>& WheeledVehicle::GetWheels() const
{
    return _wheels;
}

void WheeledVehicle::SetWheels(const Array<Wheel>& value)
{
    _wheels = value;
    Setup();
}

WheeledVehicle::EngineSettings WheeledVehicle::GetEngine() const
{
    return _engine;
}

void WheeledVehicle::SetEngine(const EngineSettings& value)
{
    _engine = value;
}

WheeledVehicle::DifferentialSettings WheeledVehicle::GetDifferential() const
{
    return _differential;
}

void WheeledVehicle::SetDifferential(const DifferentialSettings& value)
{
    _differential = value;
}

WheeledVehicle::GearboxSettings WheeledVehicle::GetGearbox() const
{
    return _gearbox;
}

void WheeledVehicle::SetGearbox(const GearboxSettings& value)
{
#if WITH_VEHICLE
    if (_vehicle)
        PhysicsBackend::SetVehicleGearbox(_vehicle, &value);
#endif
    _gearbox = value;
}

void WheeledVehicle::SetThrottle(float value)
{
    _throttle = Math::Clamp(value, -1.0f, 1.0f);
}

void WheeledVehicle::SetSteering(float value)
{
    _steering = Math::Clamp(value, -1.0f, 1.0f);
}

void WheeledVehicle::SetBrake(float value)
{
    _brake = Math::Saturate(value);
}

void WheeledVehicle::SetHandbrake(float value)
{
    _handBrake = Math::Saturate(value);
}

void WheeledVehicle::ClearInput()
{
    _throttle = 0;
    _steering = 0;
    _brake = 0;
    _handBrake = 0;
}

float WheeledVehicle::GetForwardSpeed() const
{
#if WITH_VEHICLE
    return _vehicle ? PhysicsBackend::GetVehicleForwardSpeed(_vehicle) : 0.0f;
#else
    return 0.0f;
#endif
}

float WheeledVehicle::GetSidewaysSpeed() const
{
#if WITH_VEHICLE
    return _vehicle ? PhysicsBackend::GetVehicleSidewaysSpeed(_vehicle) : 0.0f;
#else
    return 0.0f;
#endif
}

float WheeledVehicle::GetEngineRotationSpeed() const
{
#if WITH_VEHICLE
    return _vehicle && _driveType != DriveTypes::NoDrive ? PhysicsBackend::GetVehicleEngineRotationSpeed(_vehicle) : 0.0f;
#else
    return 0.0f;
#endif
}

int32 WheeledVehicle::GetCurrentGear() const
{
#if WITH_VEHICLE
    return _vehicle && _driveType != DriveTypes::NoDrive ? PhysicsBackend::GetVehicleCurrentGear(_vehicle) : 0;
#else
    return 0;
#endif
}

void WheeledVehicle::SetCurrentGear(int32 value)
{
#if WITH_VEHICLE
    if (_vehicle && _driveType != DriveTypes::NoDrive)
        PhysicsBackend::SetVehicleCurrentGear(_vehicle, value);
#endif
}

int32 WheeledVehicle::GetTargetGear() const
{
#if WITH_VEHICLE
    return _vehicle && _driveType != DriveTypes::NoDrive ? PhysicsBackend::GetVehicleTargetGear(_vehicle) : 0;
#else
    return 0;
#endif
}

void WheeledVehicle::SetTargetGear(int32 value)
{
#if WITH_VEHICLE
    if (_vehicle && _driveType != DriveTypes::NoDrive)
        PhysicsBackend::SetVehicleTargetGear(_vehicle, value);
#endif
}

void WheeledVehicle::GetWheelState(int32 index, WheelState& result)
{
    if (index >= 0 && index < _wheels.Count())
    {
        const auto collider = _wheels[index].Collider.Get();
        for (auto& wheelData : _wheelsData)
        {
            if (wheelData.Collider == collider)
            {
                result = wheelData.State;
                return;
            }
        }
    }
}

void WheeledVehicle::Setup()
{
#if WITH_VEHICLE
    if (!_actor || !IsDuringPlay())
        return;

    // Release previous
    if (_vehicle)
    {
        PhysicsBackend::RemoveVehicle(GetPhysicsScene()->GetPhysicsScene(), this);
        PhysicsBackend::DestroyVehicle(_vehicle, (int32)_driveTypeCurrent);
        _vehicle = nullptr;
    }

    // Create a new one
    _wheelsData.Clear();
    _vehicle = PhysicsBackend::CreateVehicle(this);
    if (!_vehicle)
        return;
    _driveTypeCurrent = _driveType;
    PhysicsBackend::AddVehicle(GetPhysicsScene()->GetPhysicsScene(), this);
    PhysicsBackend::SetRigidDynamicActorSolverIterationCounts(_actor, 12, 4);
#else
    LOG(Fatal, "Vehicles are not supported.");
#endif
}

#if USE_EDITOR

void WheeledVehicle::DrawPhysicsDebug(RenderView& view)
{
    // Wheels shapes
    for (auto& data : _wheelsData)
    {
        int32 wheelIndex = 0;
        for (; wheelIndex < _wheels.Count(); wheelIndex++)
        {
            if (_wheels[wheelIndex].Collider == data.Collider)
                break;
        }
        if (wheelIndex == _wheels.Count())
            break;
        auto& wheel = _wheels[wheelIndex];
        if (wheel.Collider && wheel.Collider->GetParent() == this && !wheel.Collider->GetIsTrigger())
        {
            const Vector3 currentPos = wheel.Collider->GetPosition();
            const Vector3 basePos = currentPos - Vector3(0, data.State.SuspensionOffset, 0);
            DEBUG_DRAW_WIRE_SPHERE(BoundingSphere(basePos, wheel.Radius * 0.07f), Color::Blue * 0.3f, 0, true);
            DEBUG_DRAW_WIRE_SPHERE(BoundingSphere(currentPos, wheel.Radius * 0.08f), Color::Blue * 0.8f, 0, true);
            DEBUG_DRAW_LINE(basePos, currentPos, Color::Blue, 0, true);
            DEBUG_DRAW_WIRE_CYLINDER(currentPos, wheel.Collider->GetOrientation(), wheel.Radius, wheel.Width, Color::Red * 0.8f, 0, true);
            if (!data.State.IsInAir)
            {
                DEBUG_DRAW_WIRE_SPHERE(BoundingSphere(data.State.TireContactPoint, 5.0f), Color::Green, 0, true);
            }
        }
    }
}

void WheeledVehicle::OnDebugDrawSelected()
{
    // Wheels shapes
    for (auto& data : _wheelsData)
    {
        int32 wheelIndex = 0;
        for (; wheelIndex < _wheels.Count(); wheelIndex++)
        {
            if (_wheels[wheelIndex].Collider == data.Collider)
                break;
        }
        if (wheelIndex == _wheels.Count())
            break;
        auto& wheel = _wheels[wheelIndex];
        if (wheel.Collider && wheel.Collider->GetParent() == this && !wheel.Collider->GetIsTrigger())
        {
            const Vector3 currentPos = wheel.Collider->GetPosition();
            const Vector3 basePos = currentPos - Vector3(0, data.State.SuspensionOffset, 0);
            Transform actorPose = Transform::Identity, shapePose = Transform::Identity;
            PhysicsBackend::GetRigidActorPose(_actor, actorPose.Translation, actorPose.Orientation);
            PhysicsBackend::GetShapeLocalPose(wheel.Collider->GetPhysicsShape(), shapePose.Translation, shapePose.Orientation);
            DEBUG_DRAW_WIRE_SPHERE(BoundingSphere(basePos, wheel.Radius * 0.07f), Color::Blue * 0.3f, 0, false);
            DEBUG_DRAW_WIRE_SPHERE(BoundingSphere(currentPos, wheel.Radius * 0.08f), Color::Blue * 0.8f, 0, false);
            DEBUG_DRAW_WIRE_SPHERE(BoundingSphere(actorPose.LocalToWorld(shapePose.Translation), wheel.Radius * 0.11f), Color::OrangeRed * 0.8f, 0, false);
            DEBUG_DRAW_LINE(basePos, currentPos, Color::Blue, 0, false);
            DEBUG_DRAW_WIRE_CYLINDER(currentPos, wheel.Collider->GetOrientation(), wheel.Radius, wheel.Width, Color::Red * 0.4f, 0, false);
            if (!data.State.SuspensionTraceStart.IsZero())
            {
                DEBUG_DRAW_WIRE_SPHERE(BoundingSphere(data.State.SuspensionTraceStart, 5.0f), Color::AliceBlue, 0, false);
                DEBUG_DRAW_LINE(data.State.SuspensionTraceStart, data.State.SuspensionTraceEnd, data.State.IsInAir ? Color::Red : Color::Green, 0, false);
            }
            if (!data.State.IsInAir)
            {
                DEBUG_DRAW_WIRE_SPHERE(BoundingSphere(data.State.TireContactPoint, 5.0f), Color::Green, 0, false);
            }
        }
    }

    // Center of mass
    DEBUG_DRAW_WIRE_SPHERE(BoundingSphere(_transform.LocalToWorld(_centerOfMassOffset), 10.0f), Color::Blue, 0, false);

    RigidBody::OnDebugDrawSelected();
}

#endif

void WheeledVehicle::Serialize(SerializeStream& stream, const void* otherObj)
{
    RigidBody::Serialize(stream, otherObj);

    SERIALIZE_GET_OTHER_OBJ(WheeledVehicle);

    SERIALIZE_MEMBER(DriveType, _driveType);
    SERIALIZE_MEMBER(Wheels, _wheels);
    SERIALIZE(UseReverseAsBrake);
    SERIALIZE(UseAnalogSteering);
    SERIALIZE_MEMBER(Engine, _engine);
    SERIALIZE_MEMBER(Differential, _differential);
    SERIALIZE_MEMBER(Gearbox, _gearbox);
}

void WheeledVehicle::Deserialize(DeserializeStream& stream, ISerializeModifier* modifier)
{
    RigidBody::Deserialize(stream, modifier);

    DESERIALIZE_MEMBER(DriveType, _driveType);
    DESERIALIZE_MEMBER(Wheels, _wheels);
    DESERIALIZE(UseReverseAsBrake);
    DESERIALIZE(UseAnalogSteering);
    DESERIALIZE_MEMBER(Engine, _engine);
    DESERIALIZE_MEMBER(Differential, _differential);
    DESERIALIZE_MEMBER(Gearbox, _gearbox);
}

void WheeledVehicle::OnColliderChanged(Collider* c)
{
    RigidBody::OnColliderChanged(c);

    // Rebuild vehicle when someone adds/removed wheels
    Setup();
}

void WheeledVehicle::OnPhysicsSceneChanged(PhysicsScene* previous)
{
    RigidBody::OnPhysicsSceneChanged(previous);

#if WITH_VEHICLE
    PhysicsBackend::RemoveVehicle(previous->GetPhysicsScene(), this);
    PhysicsBackend::AddVehicle(GetPhysicsScene()->GetPhysicsScene(), this);
#endif
}

void WheeledVehicle::BeginPlay(SceneBeginData* data)
{
    RigidBody::BeginPlay(data);

#if WITH_VEHICLE
    Setup();
#endif

#if USE_EDITOR
    GetSceneRendering()->AddPhysicsDebug<WheeledVehicle, &WheeledVehicle::DrawPhysicsDebug>(this);
#endif
}

void WheeledVehicle::EndPlay()
{
#if USE_EDITOR
    GetSceneRendering()->RemovePhysicsDebug<WheeledVehicle, &WheeledVehicle::DrawPhysicsDebug>(this);
#endif

#if WITH_VEHICLE
    if (_vehicle)
    {
        // Parkway Drive
        PhysicsBackend::RemoveVehicle(GetPhysicsScene()->GetPhysicsScene(), this);
        PhysicsBackend::DestroyVehicle(_vehicle, (int32)_driveTypeCurrent);
        _vehicle = nullptr;
    }
#endif

    RigidBody::EndPlay();
}
