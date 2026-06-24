using System;
using System.Collections.Generic;
using UnityEngine;

namespace Habitales.Dialogue
{
    [Serializable]
    public class TraitStickerEntry
    {
        public WorkerTrait trait;
        public StickerSO sticker;
    }

    [CreateAssetMenu(fileName = "StickerLibrary", menuName = "Habitales/Dialogue/Sticker Library")]
    public class StickerLibrary : ScriptableObject
    {
        [Header("Player Tray")]
        public List<StickerSO> playerStickers = new List<StickerSO>();
        public StickerSO birthdayStickerUnlock;

        [Header("Worker Responses (one per trait)")]
        public List<TraitStickerEntry> traitResponses = new List<TraitStickerEntry>();

        [Header("Birthday Worker Responses (one per trait)")]
        public List<TraitStickerEntry> birthdayTraitResponses = new List<TraitStickerEntry>();

        // Runtime lookup cache — built on first access
        private Dictionary<WorkerTrait, StickerSO> _traitResponseMap;
        private Dictionary<WorkerTrait, StickerSO> _birthdayResponseMap;

        public StickerSO GetTraitResponse(WorkerTrait trait)
        {
            if (_traitResponseMap == null) BuildCaches();
            _traitResponseMap.TryGetValue(trait, out StickerSO sticker);
            return sticker;
        }

        public StickerSO GetBirthdayTraitResponse(WorkerTrait trait)
        {
            if (_birthdayResponseMap == null) BuildCaches();
            _birthdayResponseMap.TryGetValue(trait, out StickerSO sticker);
            return sticker;
        }

        private void BuildCaches()
        {
            _traitResponseMap = new Dictionary<WorkerTrait, StickerSO>();
            foreach (var entry in traitResponses)
                _traitResponseMap[entry.trait] = entry.sticker;

            _birthdayResponseMap = new Dictionary<WorkerTrait, StickerSO>();
            foreach (var entry in birthdayTraitResponses)
                _birthdayResponseMap[entry.trait] = entry.sticker;
        }
    }
}