using UnityEngine;

namespace Habitales.Dialogue
{
    [CreateAssetMenu(fileName = "WorkerTemplate_New", menuName = "Habitales/Dialogue/Worker Message Template")]
    public class WorkerMessageTemplateSO : DialogueThreadSO
    {
        [Header("Worker Trait Affinity")]
        public bool isUniversal = false;
        public WorkerTrait traitAffinity;
    }
}