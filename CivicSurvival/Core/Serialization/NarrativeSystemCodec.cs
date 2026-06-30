using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Unity.Entities;

namespace CivicSurvival.Core.Serialization
{
    public readonly struct NarrativeCharacterPersistState
    {
        public NarrativeCharacterPersistState(
            string id,
            byte state,
            float relationship,
            bool isActive,
            float lastReactionTime,
            int blackoutCount,
            float angryUntilTime,
            bool isInDistrictBlackout,
            bool hasBoundEntity,
            Entity boundEntity)
        {
            Id = id ?? string.Empty;
            State = state;
            Relationship = relationship;
            IsActive = isActive;
            LastReactionTime = lastReactionTime;
            BlackoutCount = blackoutCount;
            AngryUntilTime = angryUntilTime;
            IsInDistrictBlackout = isInDistrictBlackout;
            HasBoundEntity = hasBoundEntity && boundEntity != Entity.Null;
            BoundEntity = HasBoundEntity ? boundEntity : Entity.Null;
        }

        public string Id { get; }
        public byte State { get; }
        public float Relationship { get; }
        public bool IsActive { get; }
        public float LastReactionTime { get; }
        public int BlackoutCount { get; }
        public float AngryUntilTime { get; }
        public bool IsInDistrictBlackout { get; }
        public bool HasBoundEntity { get; }
        public Entity BoundEntity { get; }
    }

    public readonly struct NarrativePersistState
    {
        public NarrativePersistState(NarrativeCharacterPersistState[] characters, int lastDecayDay)
        {
            Characters = characters ?? Array.Empty<NarrativeCharacterPersistState>();
            LastDecayDay = lastDecayDay;
        }

        public IReadOnlyList<NarrativeCharacterPersistState> Characters { get; }
        public int LastDecayDay { get; }
    }

    public static class NarrativeSystemCodec
    {
        public const float ReadyForImmediateReactionTime = -1000000f;
        public const byte MaxCharacterState = 3;
        public const int MaxDecayDay = 100000;

        public static void Write<TWriter>(in NarrativePersistState state, TWriter writer)
            where TWriter : IWriter
        {
            KeyedSerializer.WriteBlockHeader(writer, 2);

            KeyedSerializer.WriteBufferHeader(writer, "characters", state.Characters.Count);
            for (int i = 0; i < state.Characters.Count; i++)
            {
                var character = state.Characters[i];
                KeyedSerializer.WriteBlockHeader(writer, 9);
                KeyedSerializer.WriteField(writer, "id", character.Id);
                KeyedSerializer.WriteEnumByteField(writer, "state", character.State);
                KeyedSerializer.WriteField(writer, "relationship", character.Relationship);
                KeyedSerializer.WriteField(writer, "isActive", character.IsActive);
                KeyedSerializer.WriteField(writer, "lastReactionTime", character.LastReactionTime);
                KeyedSerializer.WriteField(writer, "blackoutCount", character.BlackoutCount);
                KeyedSerializer.WriteField(writer, "angryUntilTime", character.AngryUntilTime);
                KeyedSerializer.WriteField(writer, "isInDistrictBlackout", character.IsInDistrictBlackout);
                KeyedSerializer.WriteEntityField(writer, "boundEntity", character.HasBoundEntity ? character.BoundEntity : Entity.Null);
            }

            KeyedSerializer.WriteField(writer, "lastDecayDay", state.LastDecayDay);
        }

        public static void Read<TReader>(
            TReader reader,
            int maxCharacters,
            int maxBlackoutCount,
            out NarrativePersistState state)
            where TReader : IReader
        {
            NarrativeCharacterPersistState[] characters = Array.Empty<NarrativeCharacterPersistState>();
            int lastDecayDay = -1;

            int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
            for (int i = 0; i < fieldCount; i++)
            {
                var tag = KeyedSerializer.ReadFieldHeader(reader, out var key);
                switch (key)
                {
                    case "characters":
                        characters = ReadCharacters(reader, tag, maxCharacters, maxBlackoutCount);
                        break;
                    case "lastDecayDay":
                        lastDecayDay = KeyedSerializer.ReadBoundedInt(reader, tag, "lastDecayDay", -1, MaxDecayDay, -1);
                        break;
                    default:
                        KeyedSerializer.Skip(reader, tag);
                        break;
                }
            }

            state = new NarrativePersistState(characters, lastDecayDay);
        }

        private static NarrativeCharacterPersistState[] ReadCharacters<TReader>(
            TReader reader,
            TypeTag tag,
            int maxCharacters,
            int maxBlackoutCount)
            where TReader : IReader
        {
            int count = KeyedSerializer.ReadBufferCount(reader, tag, "characters", maxCharacters);
            var characters = new NarrativeCharacterPersistState[count];
            for (int i = 0; i < count; i++)
            {
                string characterId = string.Empty;
                byte characterState = 0;
                float relationship = 0f;
                bool isActive = true;
                float lastReactionTime = ReadyForImmediateReactionTime;
                int blackoutCount = 0;
                float angryUntilTime = 0f;
                bool isInDistrictBlackout = false;
                Entity boundEntity = Entity.Null;

                int fieldCount = KeyedSerializer.ReadBlockFieldCount(reader);
                for (int f = 0; f < fieldCount; f++)
                {
                    var fieldTag = KeyedSerializer.ReadFieldHeader(reader, out var fieldKey);
                    switch (fieldKey)
                    {
                        case "id":
                            characterId = KeyedSerializer.ReadString(reader, fieldTag, "id");
                            break;
                        case "state":
                            characterState = ReadCharacterState(reader, fieldTag);
                            break;
                        case "relationship":
                            relationship = KeyedSerializer.ReadSafeFloat(reader, fieldTag, "relationship", -100f, 100f, 0f);
                            break;
                        case "isActive":
                            isActive = KeyedSerializer.ReadBool(reader, fieldTag, "isActive", true);
                            break;
                        case "lastReactionTime":
                            lastReactionTime = KeyedSerializer.ReadSafeFloatUnclamped(reader, fieldTag, "lastReactionTime", ReadyForImmediateReactionTime);
                            break;
                        case "blackoutCount":
                            blackoutCount = KeyedSerializer.ReadBoundedInt(reader, fieldTag, "blackoutCount", 0, maxBlackoutCount, 0);
                            break;
                        case "angryUntilTime":
                            angryUntilTime = KeyedSerializer.ReadSafeFloatUnclamped(reader, fieldTag, "angryUntilTime", 0f);
                            break;
                        case "isInDistrictBlackout":
                            isInDistrictBlackout = KeyedSerializer.ReadBool(reader, fieldTag, "isInDistrictBlackout", false);
                            break;
                        case "boundEntity":
                            boundEntity = KeyedSerializer.ReadEntity(reader, fieldTag, "boundEntity");
                            break;
                        default:
                            KeyedSerializer.Skip(reader, fieldTag);
                            break;
                    }
                }

                characters[i] = new NarrativeCharacterPersistState(
                    characterId,
                    characterState,
                    relationship,
                    isActive,
                    lastReactionTime,
                    blackoutCount,
                    angryUntilTime,
                    isInDistrictBlackout,
                    boundEntity != Entity.Null,
                    boundEntity);
            }

            return characters;
        }

        private static byte ReadCharacterState<TReader>(TReader reader, TypeTag tag)
            where TReader : IReader
        {
            if (!KeyedSerializer.ExpectTag(reader, tag, TypeTag.EnumByte, "state"))
                return 0;

            reader.Read(out byte value);
            return value <= MaxCharacterState ? value : (byte)0;
        }
    }
}
