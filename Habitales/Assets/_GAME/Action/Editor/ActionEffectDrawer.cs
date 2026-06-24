using Habitales.Actions;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

// ActionEffectDrawer — conditional show/hide for the two ActionEffect slots.
//
// We deliberately do NOT use [EnableIf] for this. EnableIf works for top-level SO fields
// (TileEntitySO uses it that way) but is unreliable on a field nested inside a struct that
// is itself a reorderable list element — which is exactly ActionSO.effects. There the
// irrelevant slot kept showing. This drawer drives the visibility by hand instead, so the
// behaviour is deterministic regardless of how the list is rendered.
//
// Registering a CustomPropertyDrawer also means the Artifice list view picks this up through
// its HasCustomDrawer / CreatePropertyGUI path, so the look stays consistent with the rest
// of the inspector.
namespace Habitales.Actions.EditorTools
{
    [CustomPropertyDrawer(typeof(ActionEffect))]
    public class ActionEffectDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();

            var typeProp = property.FindPropertyRelative(nameof(ActionEffect.type));
            var entityProp = property.FindPropertyRelative(nameof(ActionEffect.entityToPlace));
            var behaviourProp = property.FindPropertyRelative(nameof(ActionEffect.customBehaviour));

            var typeField = new PropertyField(typeProp);
            var entityField = new PropertyField(entityProp);
            var behaviourField = new PropertyField(behaviourProp);

            root.Add(typeField);
            root.Add(entityField);
            root.Add(behaviourField);

            void Refresh()
            {
                var isPlaceEntity = typeProp.enumValueIndex == (int)ActionEffectType.PlaceEntity;
                entityField.style.display = isPlaceEntity ? DisplayStyle.Flex : DisplayStyle.None;
                behaviourField.style.display = isPlaceEntity ? DisplayStyle.None : DisplayStyle.Flex;
            }

            Refresh();
            // Re-evaluate whenever the type dropdown changes (also fires on undo/redo & external edits).
            typeField.RegisterValueChangeCallback(_ => Refresh());

            return root;
        }
    }
}
