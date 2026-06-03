using System.Collections.Generic;
using UnityEngine;

namespace Habitales.Dialogue
{
    [CreateAssetMenu(fileName = "DialogueRegistry", menuName = "Habitales/Dialogue/Dialogue Registry")]
    public class DialogueRegistry : ScriptableObject
    {
        [Header("Tabs (Fixed Cast Only)")]
        public List<DialogueTabSO> tabs = new List<DialogueTabSO>();

        [Header("Authored Threads")]
        public List<DialogueThreadSO> threads = new List<DialogueThreadSO>();

        [Header("Worker Templates")]
        public List<WorkerMessageTemplateSO> workerTemplates = new List<WorkerMessageTemplateSO>();

        [Header("Worker Portrait Fallback")]
        public Sprite fallbackWorkerPortrait;

        // Runtime lookup caches — built on first access
        private Dictionary<string, DialogueTabSO> _tabMap;
        private Dictionary<string, DialogueThreadSO> _threadMap;

        public DialogueTabSO GetTab(string tabID)
        {
            if (_tabMap == null) BuildCaches();
            _tabMap.TryGetValue(tabID, out DialogueTabSO tab);
            return tab;
        }

        public DialogueThreadSO GetThread(string threadID)
        {
            if (_threadMap == null) BuildCaches();
            _threadMap.TryGetValue(threadID, out DialogueThreadSO thread);
            return thread;
        }

        public DialogueTabSO GetGroupTab()
        {
            if (_tabMap == null) BuildCaches();
            foreach (var tab in tabs)
                if (tab.isGroupChat) return tab;
            return null;
        }

        private void BuildCaches()
        {
            _tabMap = new Dictionary<string, DialogueTabSO>();
            foreach (var tab in tabs)
                if (tab != null) _tabMap[tab.tabID] = tab;

            _threadMap = new Dictionary<string, DialogueThreadSO>();
            foreach (var thread in threads)
                if (thread != null) _threadMap[thread.threadID] = thread;
        }
    }
}