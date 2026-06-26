using UnityEngine;
using UnityEngine.UI;

namespace Habitales.UI.Actions
{
    /// <summary>
    /// Passive view: the lock-card modal dialog. Show() and Hide() are called by the controller;
    /// the Continue button wires itself to Hide() in OnEnable so the modal can self-dismiss.
    /// </summary>
    public class LockModalView : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Button continueButton;

        void Awake()
        {
            if (root != null) root.SetActive(false);
        }

        void OnEnable()
        {
            continueButton?.onClick.AddListener(Hide);
        }

        void OnDisable()
        {
            continueButton?.onClick.RemoveAllListeners();
        }

        /// <summary>Makes the lock modal visible.</summary>
        public void Show() { if (root != null) root.SetActive(true); }

        /// <summary>Hides the lock modal.</summary>
        public void Hide() { if (root != null) root.SetActive(false); }
    }
}
