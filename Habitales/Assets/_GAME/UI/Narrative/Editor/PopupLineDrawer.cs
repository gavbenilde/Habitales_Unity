#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Habitales.UI.Editor
{
    // ─────────────────────────────────────────────────────────────────────────
    // PopupLineDrawer.cs — IMGUI property drawer for PopupLine.
    //
    // Collapses the customName row unless speaker == PopupSpeaker.Custom, so
    // the list in PopupSO stays compact for Azi/Bob lines. GetPropertyHeight
    // is matched so multi-element lists lay out correctly.
    //
    // Editor-only (inside Editor/ folder + #if UNITY_EDITOR guard).
    // Added 2026-06-30 (WO-1, EventManager → TriggerManager rework).
    // ─────────────────────────────────────────────────────────────────────────

    [CustomPropertyDrawer(typeof(PopupLine))]
    public class PopupLineDrawer : PropertyDrawer
    {
        // ── Row heights ───────────────────────────────────────────────────────

        private const float Pad    = 2f;   // gap between rows
        private const float Single = 18f;  // standard single-line field height

        // TextArea min height (2 lines @ ~18px + scrollbar allowance).
        private const float TextAreaMin = 40f;

        // ── DrawerImpl ────────────────────────────────────────────────────────

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var speakerProp  = property.FindPropertyRelative("speaker");
            var customProp   = property.FindPropertyRelative("customName");
            var overrideProp = property.FindPropertyRelative("portraitOverride");
            var bodyProp     = property.FindPropertyRelative("body");

            bool isCustom = speakerProp.enumValueIndex == (int)PopupSpeaker.Custom;

            float y = position.y;

            // ── speaker row ──────────────────────────────────────────────────
            Rect row = new Rect(position.x, y, position.width, Single);
            EditorGUI.PropertyField(row, speakerProp);
            y += Single + Pad;

            // ── customName row (only when Custom) ────────────────────────────
            if (isCustom)
            {
                row = new Rect(position.x, y, position.width, Single);
                EditorGUI.PropertyField(row, customProp, new GUIContent("Custom Name"));
                y += Single + Pad;
            }

            // ── portraitOverride row ─────────────────────────────────────────
            row = new Rect(position.x, y, position.width, Single);
            EditorGUI.PropertyField(row, overrideProp, new GUIContent("Portrait Override"));
            y += Single + Pad;

            // ── body row (TextArea) ───────────────────────────────────────────
            float bodyHeight = Mathf.Max(TextAreaMin, EditorGUI.GetPropertyHeight(bodyProp, true));
            row = new Rect(position.x, y, position.width, bodyHeight);
            EditorGUI.PropertyField(row, bodyProp);

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var speakerProp = property.FindPropertyRelative("speaker");
            var bodyProp    = property.FindPropertyRelative("body");

            bool isCustom = speakerProp.enumValueIndex == (int)PopupSpeaker.Custom;

            // speaker + portraitOverride always visible (2 rows)
            float h = (Single + Pad) * 2f;

            // customName row only when Custom
            if (isCustom)
                h += Single + Pad;

            // body TextArea (may be taller if the designer typed a lot)
            float bodyHeight = Mathf.Max(TextAreaMin, EditorGUI.GetPropertyHeight(bodyProp, true));
            h += bodyHeight;

            return h;
        }
    }
}
#endif
