// Copyright (c) 2012-2022 Wojciech Figat. All rights reserved.

#include "Animation.h"
#include "SkinnedModel.h"
#include "Engine/Core/Log.h"
#include "Engine/Profiler/ProfilerCPU.h"
#include "Engine/Content/Factories/BinaryAssetFactory.h"
#include "Engine/Animations/CurveSerialization.h"
#include "Engine/Animations/AnimEvent.h"
#include "Engine/Scripting/Scripting.h"
#include "Engine/Threading/Threading.h"
#include "Engine/Serialization/MemoryReadStream.h"
#if USE_EDITOR
#include "Engine/Serialization/MemoryWriteStream.h"
#include "Engine/Level/Level.h"
#endif

REGISTER_BINARY_ASSET(Animation, "FlaxEngine.Animation", false);

Animation::Animation(const SpawnParams& params, const AssetInfo* info)
    : BinaryAsset(params, info)
{
}

#if USE_EDITOR

void Animation::OnScriptsReloadStart()
{
    for (auto& e : Events)
    {
        for (auto& k : e.Second.GetKeyframes())
        {
            Level::ScriptsReloadRegisterObject((ScriptingObject*&)k.Value.Instance);
        }
    }
}

#endif

Animation::InfoData Animation::GetInfo() const
{
    ScopeLock lock(Locker);
    InfoData info;
    info.MemoryUsage = sizeof(Animation);
    if (IsLoaded())
    {
        info.Length = Data.GetLength();
        info.FramesCount = (int32)Data.Duration;
        info.ChannelsCount = Data.Channels.Count();
        info.KeyframesCount = Data.GetKeyframesCount();
        info.MemoryUsage += Data.Channels.Capacity() * sizeof(NodeAnimationData);
        for (auto& e : Data.Channels)
        {
            info.MemoryUsage += (e.NodeName.Length() + 1) * sizeof(Char);
            info.MemoryUsage += e.Position.GetKeyframes().Capacity() * sizeof(LinearCurveKeyframe<Vector3>);
            info.MemoryUsage += e.Rotation.GetKeyframes().Capacity() * sizeof(LinearCurveKeyframe<Quaternion>);
            info.MemoryUsage += e.Scale.GetKeyframes().Capacity() * sizeof(LinearCurveKeyframe<Vector3>);
        }
    }
    else
    {
        info.Length = 0.0f;
        info.FramesCount = 0;
        info.ChannelsCount = 0;
        info.KeyframesCount = 0;
    }
    info.MemoryUsage += MappingCache.Capacity() * (sizeof(void*) + sizeof(NodeToChannel) + 1);
    for (auto& e : MappingCache)
        info.MemoryUsage += e.Value.Capacity() * sizeof(int32);
    return info;
}

void Animation::ClearCache()
{
    ScopeLock lock(Locker);

    // Unlink events
    for (auto i = MappingCache.Begin(); i.IsNotEnd(); ++i)
    {
        i->Key->OnUnloaded.Unbind<Animation, &Animation::OnSkinnedModelUnloaded>(this);
        i->Key->OnReloading.Unbind<Animation, &Animation::OnSkinnedModelUnloaded>(this);
    }

    // Free memory
    MappingCache.Clear();
    MappingCache.Cleanup();
}

const Animation::NodeToChannel* Animation::GetMapping(SkinnedModel* obj)
{
    ASSERT(obj && obj->IsLoaded() && IsLoaded());

    ScopeLock lock(Locker);

    // Try quick lookup
    NodeToChannel* result = MappingCache.TryGet(obj);
    if (result == nullptr)
    {
        PROFILE_CPU();

        // Add to cache
        NodeToChannel tmp;
        auto bucket = MappingCache.Add(obj, tmp);
        result = &bucket->Value;
        obj->OnUnloaded.Bind<Animation, &Animation::OnSkinnedModelUnloaded>(this);
        obj->OnReloading.Bind<Animation, &Animation::OnSkinnedModelUnloaded>(this);

        // Initialize the mapping
        const auto& skeleton = obj->Skeleton;
        const int32 nodesCount = skeleton.Nodes.Count();
        result->Resize(nodesCount, false);
        result->SetAll(-1);
        for (int32 i = 0; i < Data.Channels.Count(); i++)
        {
            auto& nodeAnim = Data.Channels[i];

            for (int32 j = 0; j < nodesCount; j++)
            {
                if (skeleton.Nodes[j].Name == nodeAnim.NodeName)
                {
                    result->At(j) = i;
                    break;
                }
            }
        }
    }

    return result;
}

#if USE_EDITOR

void Animation::LoadTimeline(BytesContainer& result) const
{
    result.Release();
    if (!IsLoaded())
        return;
    MemoryWriteStream stream(4096);

    // Version
    stream.WriteInt32(4);

    // Meta
    float fps = (float)Data.FramesPerSecond;
    const float fpsInv = 1.0f / fps;
    stream.WriteFloat(fps);
    stream.WriteInt32((int32)Data.Duration);
    int32 tracksCount = Data.Channels.Count() + Events.Count();
    for (auto& channel : Data.Channels)
        tracksCount +=
                (channel.Position.GetKeyframes().HasItems() ? 1 : 0) +
                (channel.Rotation.GetKeyframes().HasItems() ? 1 : 0) +
                (channel.Scale.GetKeyframes().HasItems() ? 1 : 0);
    stream.WriteInt32(tracksCount);

    // Tracks
    int32 trackIndex = 0;
    for (int32 i = 0; i < Data.Channels.Count(); i++)
    {
        auto& channel = Data.Channels[i];
        const int32 childrenCount =
                (channel.Position.GetKeyframes().HasItems() ? 1 : 0) +
                (channel.Rotation.GetKeyframes().HasItems() ? 1 : 0) +
                (channel.Scale.GetKeyframes().HasItems() ? 1 : 0);

        // Animation Channel track
        stream.WriteByte(17); // Track Type
        stream.WriteByte(0); // Track Flags
        stream.WriteInt32(-1); // Parent Index
        stream.WriteInt32(childrenCount); // Children Count
        stream.WriteString(channel.NodeName, -13); // Name
        stream.Write(&Color32::White); // Color
        const int32 parentIndex = trackIndex++;

        auto& position = channel.Position.GetKeyframes();
        if (position.HasItems())
        {
            // Animation Channel Data track (position)
            stream.WriteByte(18); // Track Type
            stream.WriteByte(0); // Track Flags
            stream.WriteInt32(parentIndex); // Parent Index
            stream.WriteInt32(0); // Children Count
            stream.WriteString(String::Format(TEXT("Track_{0}_Position"), i), -13); // Name
            stream.Write(&Color32::White); // Color
            stream.WriteByte(0); // Type
            stream.WriteInt32(position.Count()); // Keyframes Count
            for (auto& k : position)
            {
                stream.WriteFloat(k.Time * fpsInv);
                stream.Write(&k.Value);
            }
            trackIndex++;
        }

        auto& rotation = channel.Rotation.GetKeyframes();
        if (rotation.HasItems())
        {
            // Animation Channel Data track (rotation)
            stream.WriteByte(18); // Track Type
            stream.WriteByte(0); // Track Flags
            stream.WriteInt32(parentIndex); // Parent Index
            stream.WriteInt32(0); // Children Count
            stream.WriteString(String::Format(TEXT("Track_{0}_Rotation"), i), -13); // Name
            stream.Write(&Color32::White); // Color
            stream.WriteByte(1); // Type
            stream.WriteInt32(rotation.Count()); // Keyframes Count
            for (auto& k : rotation)
            {
                stream.WriteFloat(k.Time * fpsInv);
                stream.Write(&k.Value);
            }
            trackIndex++;
        }

        auto& scale = channel.Scale.GetKeyframes();
        if (scale.HasItems())
        {
            // Animation Channel Data track (scale)
            stream.WriteByte(18); // Track Type
            stream.WriteByte(0); // Track Flags
            stream.WriteInt32(parentIndex); // Parent Index
            stream.WriteInt32(0); // Children Count
            stream.WriteString(String::Format(TEXT("Track_{0}_Scale"), i), -13); // Name
            stream.Write(&Color32::White); // Color
            stream.WriteByte(2); // Type
            stream.WriteInt32(scale.Count()); // Keyframes Count
            for (auto& k : scale)
            {
                stream.WriteFloat(k.Time * fpsInv);
                stream.Write(&k.Value);
            }
            trackIndex++;
        }
    }
    for (auto& e : Events)
    {
        // Animation Event track
        stream.WriteByte(19); // Track Type
        stream.WriteByte(0); // Track Flags
        stream.WriteInt32(-1); // Parent Index
        stream.WriteInt32(0); // Children Count
        stream.WriteString(e.First, -13); // Name
        stream.Write(&Color32::White); // Color
        stream.WriteInt32(e.Second.GetKeyframes().Count()); // Events Count
        for (const auto& k : e.Second.GetKeyframes())
        {
            stream.WriteFloat(k.Time);
            stream.WriteFloat(k.Value.Duration);
            stream.WriteStringAnsi(k.Value.TypeName, 13);
            stream.WriteJson(k.Value.Instance);
        }
    }

    result.Copy(stream.GetHandle(), stream.GetPosition());
}

bool Animation::SaveTimeline(BytesContainer& data)
{
    // Wait for asset to be loaded or don't if last load failed (eg. by shader source compilation error)
    if (LastLoadFailed())
    {
        LOG(Warning, "Saving asset that failed to load.");
    }
    else if (WaitForLoaded())
    {
        LOG(Error, "Asset loading failed. Cannot save it.");
        return true;
    }
    ScopeLock lock(Locker);
    MemoryReadStream stream(data.Get(), data.Length());

    // Version
    int32 version;
    stream.ReadInt32(&version);
    switch (version)
    {
    case 3: // [Deprecated on 03.09.2021 expires on 03.09.2023]
    case 4:
    {
        // Meta
        float fps;
        stream.ReadFloat(&fps);
        Data.FramesPerSecond = static_cast<double>(fps);
        int32 duration;
        stream.ReadInt32(&duration);
        Data.Duration = static_cast<double>(duration);
        int32 tracksCount;
        stream.ReadInt32(&tracksCount);

        // Tracks
        Data.Channels.Clear();
        Events.Clear();
        Dictionary<int32, int32> animationChannelTrackIndexToChannelIndex;
        animationChannelTrackIndexToChannelIndex.EnsureCapacity(tracksCount * 3);
        for (int32 trackIndex = 0; trackIndex < tracksCount; trackIndex++)
        {
            const byte trackType = stream.ReadByte();
            const byte trackFlags = stream.ReadByte();
            int32 parentIndex, childrenCount;
            stream.ReadInt32(&parentIndex);
            stream.ReadInt32(&childrenCount);
            String name;
            stream.ReadString(&name, -13);
            Color32 color;
            stream.Read(&color);
            switch (trackType)
            {
            case 17:
            {
                // Animation Channel track
                const int32 channelIndex = Data.Channels.Count();
                animationChannelTrackIndexToChannelIndex[trackIndex] = channelIndex;
                auto& channel = Data.Channels.AddOne();
                channel.NodeName = name;
                break;
            }
            case 18:
            {
                // Animation Channel Data track
                const byte type = stream.ReadByte();
                int32 keyframesCount;
                stream.ReadInt32(&keyframesCount);
                int32 channelIndex;
                if (!animationChannelTrackIndexToChannelIndex.TryGet(parentIndex, channelIndex))
                {
                    LOG(Error, "Invalid animation channel data track parent linkage.");
                    return true;
                }
                auto& channel = Data.Channels[channelIndex];
                switch (type)
                {
                case 0:
                    channel.Position.Resize(keyframesCount);
                    for (int32 i = 0; i < keyframesCount; i++)
                    {
                        LinearCurveKeyframe<Vector3>& k = channel.Position.GetKeyframes()[i];
                        stream.ReadFloat(&k.Time);
                        k.Time *= fps;
                        stream.Read(&k.Value);
                    }
                    break;
                case 1:
                    channel.Rotation.Resize(keyframesCount);
                    for (int32 i = 0; i < keyframesCount; i++)
                    {
                        LinearCurveKeyframe<Quaternion>& k = channel.Rotation.GetKeyframes()[i];
                        stream.ReadFloat(&k.Time);
                        k.Time *= fps;
                        stream.Read(&k.Value);
                    }
                    break;
                case 2:
                    channel.Scale.Resize(keyframesCount);
                    for (int32 i = 0; i < keyframesCount; i++)
                    {
                        LinearCurveKeyframe<Vector3>& k = channel.Scale.GetKeyframes()[i];
                        stream.ReadFloat(&k.Time);
                        k.Time *= fps;
                        stream.Read(&k.Value);
                    }
                    break;
                }
                break;
            }
            case 19:
            {
                // Animation Event
                int32 count;
                stream.ReadInt32(&count);
                auto& eventTrack = Events.AddOne();
                eventTrack.First = name;
                eventTrack.Second.Resize(count);
                for (int32 i = 0; i < count; i++)
                {
                    auto& k = eventTrack.Second.GetKeyframes()[i];
                    stream.ReadFloat(&k.Time);
                    stream.ReadFloat(&k.Value.Duration);
                    stream.ReadStringAnsi(&k.Value.TypeName, 13);
                    const ScriptingTypeHandle typeHandle = Scripting::FindScriptingType(k.Value.TypeName);
                    k.Value.Instance = NewObject<AnimEvent>(typeHandle);
                    stream.ReadJson(k.Value.Instance);
                    if (!k.Value.Instance)
                    {
                        LOG(Error, "Failed to spawn object of type {0}.", String(k.Value.TypeName));
                        continue;
                    }
                    if (!_registeredForScriptingReload)
                    {
                        _registeredForScriptingReload = true;
                        Level::ScriptsReloadStart.Bind<Animation, &Animation::OnScriptsReloadStart>(this);
                    }
                }
                break;
            }
            default:
                LOG(Error, "Unsupported track type {0} for animation.", trackType);
                return true;
            }
        }
        break;
    }
    default:
        LOG(Warning, "Unknown timeline version {0}.", version);
        return true;
    }
    if (stream.GetLength() != stream.GetPosition())
    {
        LOG(Warning, "Invalid animation timeline data length.");
    }

    return Save();
}

bool Animation::Save(const StringView& path)
{
    // Wait for asset to be loaded or don't if last load failed (eg. by shader source compilation error)
    if (LastLoadFailed())
    {
        LOG(Warning, "Saving asset that failed to load.");
    }
    else if (WaitForLoaded())
    {
        LOG(Error, "Asset loading failed. Cannot save it.");
        return true;
    }

    ScopeLock lock(Locker);

    // Serialize animation data to the stream
    {
        MemoryWriteStream stream(4096);

        // Info
        stream.WriteInt32(101);
        stream.WriteDouble(Data.Duration);
        stream.WriteDouble(Data.FramesPerSecond);
        stream.WriteBool(Data.EnableRootMotion);
        stream.WriteString(Data.RootNodeName, 13);

        // Animation channels
        stream.WriteInt32(Data.Channels.Count());
        for (int32 i = 0; i < Data.Channels.Count(); i++)
        {
            auto& anim = Data.Channels[i];
            stream.WriteString(anim.NodeName, 172);
            Serialization::Serialize(stream, anim.Position);
            Serialization::Serialize(stream, anim.Rotation);
            Serialization::Serialize(stream, anim.Scale);
        }

        // Animation events
        stream.WriteInt32(Events.Count());
        for (int32 i = 0; i < Events.Count(); i++)
        {
            auto& e = Events[i];
            stream.WriteString(e.First, 172);
            stream.WriteInt32(e.Second.GetKeyframes().Count());
            for (const auto& k : e.Second.GetKeyframes())
            {
                stream.WriteFloat(k.Time);
                stream.WriteFloat(k.Value.Duration);
                stream.WriteStringAnsi(k.Value.TypeName, 17);
                stream.WriteJson(k.Value.Instance);
            }
        }

        // Set data to the chunk asset
        auto chunk0 = GetOrCreateChunk(0);
        ASSERT(chunk0 != nullptr);
        chunk0->Data.Copy(stream.GetHandle(), stream.GetPosition());
    }

    // Save
    AssetInitData data;
    data.SerializedVersion = SerializedVersion;
    const bool saveResult = path.HasChars() ? SaveAsset(path, data) : SaveAsset(data, true);
    if (saveResult)
    {
        LOG(Error, "Cannot save \'{0}\'", ToString());
        return true;
    }

    return false;
}

#endif

void Animation::OnSkinnedModelUnloaded(Asset* obj)
{
    ScopeLock lock(Locker);

    const auto key = static_cast<SkinnedModel*>(obj);
    auto i = MappingCache.Find(key);
    ASSERT(i != MappingCache.End());

    // Unlink event
    key->OnUnloaded.Unbind<Animation, &Animation::OnSkinnedModelUnloaded>(this);
    key->OnReloading.Unbind<Animation, &Animation::OnSkinnedModelUnloaded>(this);

    // Clear cache
    i->Value.Resize(0, false);
    MappingCache.Remove(i);
}

void Animation::OnScriptingDispose()
{
    // Dispose any events to prevent crashes (scripting is released before content)
    for (auto& e : Events)
    {
        for (auto& k : e.Second.GetKeyframes())
        {
            if (k.Value.Instance)
            {
                Delete(k.Value.Instance);
                k.Value.Instance = nullptr;
            }
        }
    }

    BinaryAsset::OnScriptingDispose();
}

Asset::LoadResult Animation::load()
{
    // Get stream with animations data
    const auto dataChunk = GetChunk(0);
    if (dataChunk == nullptr)
        return LoadResult::MissingDataChunk;
    MemoryReadStream stream(dataChunk->Get(), dataChunk->Size());

    // Info
    int32 headerVersion = *(int32*)stream.GetPositionHandle();
    switch (headerVersion)
    {
    case 100:
    case 101:
    {
        stream.ReadInt32(&headerVersion);
        stream.ReadDouble(&Data.Duration);
        stream.ReadDouble(&Data.FramesPerSecond);
        Data.EnableRootMotion = stream.ReadBool();
        stream.ReadString(&Data.RootNodeName, 13);
        break;
    }
    default:
        stream.ReadDouble(&Data.Duration);
        stream.ReadDouble(&Data.FramesPerSecond);
        break;
    }
    if (Data.Duration < ZeroTolerance || Data.FramesPerSecond < ZeroTolerance)
    {
        LOG(Warning, "Invalid animation info");
        return LoadResult::Failed;
    }

    // Animation channels
    int32 animationsCount;
    stream.ReadInt32(&animationsCount);
    Data.Channels.Resize(animationsCount, false);
    for (int32 i = 0; i < animationsCount; i++)
    {
        auto& anim = Data.Channels[i];

        stream.ReadString(&anim.NodeName, 172);
        bool failed = Serialization::Deserialize(stream, anim.Position);
        failed |= Serialization::Deserialize(stream, anim.Rotation);
        failed |= Serialization::Deserialize(stream, anim.Scale);

        if (failed)
        {
            LOG(Warning, "Failed to deserialize the animation curve data.");
            return LoadResult::Failed;
        }
    }

    // Animation events
    if (headerVersion >= 101)
    {
        int32 eventTracksCount;
        stream.ReadInt32(&eventTracksCount);
        Events.Resize(eventTracksCount, false);
#if !USE_EDITOR
        StringAnsi typeName;
#endif
        for (int32 i = 0; i < eventTracksCount; i++)
        {
            auto& e = Events[i];
            stream.ReadString(&e.First, 172);
            int32 eventsCount;
            stream.ReadInt32(&eventsCount);
            e.Second.GetKeyframes().Resize(eventsCount);
            for (auto& k : e.Second.GetKeyframes())
            {
                stream.ReadFloat(&k.Time);
                stream.ReadFloat(&k.Value.Duration);
#if USE_EDITOR
                StringAnsi& typeName = k.Value.TypeName;
#endif
                stream.ReadStringAnsi(&typeName, 17);
                const ScriptingTypeHandle typeHandle = Scripting::FindScriptingType(typeName);
                k.Value.Instance = NewObject<AnimEvent>(typeHandle);
                stream.ReadJson(k.Value.Instance);
                if (!k.Value.Instance)
                {
                    LOG(Error, "Failed to spawn object of type {0}.", String(typeName));
                    continue;
                }
#if USE_EDITOR
                if (!_registeredForScriptingReload)
                {
                    _registeredForScriptingReload = true;
                    Level::ScriptsReloadStart.Bind<Animation, &Animation::OnScriptsReloadStart>(this);
                }
#endif
            }
        }
    }

    return LoadResult::Ok;
}

void Animation::unload(bool isReloading)
{
#if USE_EDITOR
    if (_registeredForScriptingReload)
    {
        _registeredForScriptingReload = false;
        Level::ScriptsReloadStart.Unbind<Animation, &Animation::OnScriptsReloadStart>(this);
    }
#endif
    ClearCache();
    Data.Dispose();
    for (const auto& e : Events)
    {
        for (const auto& k : e.Second.GetKeyframes())
        {
            if (k.Value.Instance)
                Delete(k.Value.Instance);
        }
    }
    Events.Clear();
}

AssetChunksFlag Animation::getChunksToPreload() const
{
    return GET_CHUNK_FLAG(0);
}
