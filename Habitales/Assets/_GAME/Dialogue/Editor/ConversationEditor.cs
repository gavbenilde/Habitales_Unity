using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Habitales.Dialogue;

namespace Habitales.Dialogue.EditorTools
{
    [CustomEditor(typeof(ConversationSO))]
    public class ConversationEditor : UnityEditor.Editor
    {
        // Cached expression lists per speaker — rebuilt each OnInspectorGUI call so
        // it stays fresh if the user edits a CharacterProfileSO in the same session.
        private CharacterProfileSO _aziProfile;
        private CharacterProfileSO _bobProfile;
        private bool _profilesCached;

        // WorkerTrait entries + a trailing "Universal" sentinel index
        private static readonly string[] WorkerTraitNames =
        {
            "Charming", "Witty", "Boisterous", "Serious", "Cheerful",
            "Stoic", "Curious", "Grumpy", "Optimistic", "Pragmatic",
            "Universal"   // index 10 — not a real enum value
        };
        private const int UniversalIndex = 10;

        // Allowed speakers per channel
        private static DialogueSpeaker[] AllowedSpeakers(DialogueChannel channel)
        {
            switch (channel)
            {
                case DialogueChannel.GroupChat: return new[] { DialogueSpeaker.Player, DialogueSpeaker.Azi, DialogueSpeaker.Bob };
                case DialogueChannel.Azi:       return new[] { DialogueSpeaker.Player, DialogueSpeaker.Azi };
                case DialogueChannel.Bob:       return new[] { DialogueSpeaker.Player, DialogueSpeaker.Bob };
                case DialogueChannel.Worker:    return new[] { DialogueSpeaker.Player, DialogueSpeaker.Worker };
                default:                        return new[] { DialogueSpeaker.Player };
            }
        }

        // ─── profile cache ───────────────────────────────────────────────────────

        private void EnsureProfiles()
        {
            if (_profilesCached) return;
            _aziProfile = null;
            _bobProfile = null;

            string[] guids = AssetDatabase.FindAssets("t:CharacterProfileSO");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<CharacterProfileSO>(path);
                if (profile == null) continue;
                if (profile.character == DialogueSpeaker.Azi) _aziProfile = profile;
                else if (profile.character == DialogueSpeaker.Bob) _bobProfile = profile;
            }
            _profilesCached = true;
        }

        // Returns the CharacterProfileSO for a speaker (Azi/Bob only), or null.
        private CharacterProfileSO ProfileFor(DialogueSpeaker speaker)
        {
            if (speaker == DialogueSpeaker.Azi) return _aziProfile;
            if (speaker == DialogueSpeaker.Bob) return _bobProfile;
            return null;
        }

        // ─── top-level inspector ─────────────────────────────────────────────────

        public override void OnInspectorGUI()
        {
            // Refresh profile cache each repaint so edits to CharacterProfileSO
            // are picked up without needing to deselect this asset.
            _profilesCached = false;
            EnsureProfiles();

            var so = (ConversationSO)target;

            EditorGUI.BeginChangeCheck();

            // ── Trigger ──────────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Trigger", EditorStyles.boldLabel);
            var newTrigger = (DialogueTrigger)EditorGUILayout.EnumPopup("Trigger", so.trigger);

            // ── Channel ──────────────────────────────────────────────────────────
            // Manual conversations are played by direct reference (check-ins, reports)
            // and never routed to a chat tab, so the whole Routing section is moot.
            var newChannel = so.channel;
            bool newUniversal = so.universal;
            WorkerTrait newPersonality = so.personality;

            if (newTrigger == DialogueTrigger.Manual)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(
                    "Manual: played only by direct reference (check-ins, reports, popup links). " +
                    "No chat routing — do not add this conversation to the DialogueRegistry.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Routing", EditorStyles.boldLabel);
                newChannel = (DialogueChannel)EditorGUILayout.EnumPopup("Channel", so.channel);
            }

            // ── Personality (Worker channel only) ────────────────────────────────
            if (newTrigger != DialogueTrigger.Manual && newChannel == DialogueChannel.Worker)
            {
                int currentIndex = so.universal
                    ? UniversalIndex
                    : (int)so.personality;  // WorkerTrait values are 0-9

                int selectedIndex = EditorGUILayout.Popup("Personality", currentIndex, WorkerTraitNames);

                if (selectedIndex == UniversalIndex)
                {
                    newUniversal = true;
                }
                else
                {
                    newUniversal = false;
                    newPersonality = (WorkerTrait)selectedIndex;
                }
            }

            // ── Apply header mutations ────────────────────────────────────────────
            if (EditorGUI.EndChangeCheck()
                || newTrigger != so.trigger
                || newChannel != so.channel
                || newUniversal != so.universal
                || newPersonality != so.personality)
            {
                Undo.RecordObject(target, "Edit Conversation Header");
                so.trigger = newTrigger;
                so.channel = newChannel;
                so.universal = newUniversal;
                so.personality = newPersonality;
                EditorUtility.SetDirty(target);
            }

            // ── Thread ───────────────────────────────────────────────────────────
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Thread", EditorStyles.boldLabel);

            DrawNodeList(so.thread, so.channel, depth: 0);

            EditorGUILayout.Space(4);
            if (GUILayout.Button("+ New Message"))
            {
                Undo.RecordObject(target, "Add Message");
                var node = new MessageNode
                {
                    sender  = DefaultSenderForChannel(so.channel),
                    payload = new ContentPayload()
                };
                so.thread.Add(node);
                EditorUtility.SetDirty(target);
            }
        }

        // ─── node list renderer ──────────────────────────────────────────────────

        // Structural change requested by a node's row buttons. Applied AFTER the render
        // loop closes, never inline — mutating the list mid-loop during a click event
        // desyncs IMGUI's cached layout and throws "Mismatched LayoutGroup" errors.
        private enum NodeAction { None, Remove, MoveUp, MoveDown }

        /// <summary>
        /// Renders a list of MessageNodes at the given nesting depth.
        /// Passes back any mutations through direct field writes + Undo.
        /// </summary>
        private void DrawNodeList(List<MessageNode> list, DialogueChannel channel, int depth)
        {
            float indentPx = depth * 16f;

            NodeAction pendingAction = NodeAction.None;
            int pendingIndex = -1;

            // Reserve + indent via a horizontal block
            for (int i = 0; i < list.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(indentPx);
                EditorGUILayout.BeginVertical();

                var action = DrawNode(list, i, channel, depth);

                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();

                if (action != NodeAction.None) { pendingAction = action; pendingIndex = i; }
            }

            if (pendingAction != NodeAction.None)
                ApplyNodeAction(list, pendingAction, pendingIndex);
        }

        private void ApplyNodeAction(List<MessageNode> list, NodeAction action, int index)
        {
            if (index < 0 || index >= list.Count) return;
            var node = list[index];

            switch (action)
            {
                case NodeAction.Remove:
                    Undo.RecordObject(target, "Remove Message");
                    list.RemoveAt(index);
                    break;

                case NodeAction.MoveUp when index > 0:
                    Undo.RecordObject(target, "Move Message Up");
                    list.RemoveAt(index);
                    list.Insert(index - 1, node);
                    break;

                case NodeAction.MoveDown when index < list.Count - 1:
                    Undo.RecordObject(target, "Move Message Down");
                    list.RemoveAt(index);
                    list.Insert(index + 1, node);
                    break;

                default:
                    return;
            }

            EditorUtility.SetDirty(target);
        }

        /// <summary>
        /// Draws one MessageNode (list[index]) and its controls. Returns the structural
        /// action its row buttons requested; the caller applies it after the render loop.
        /// </summary>
        private NodeAction DrawNode(List<MessageNode> list, int index, DialogueChannel channel, int depth)
        {
            var node = list[index];

            // Boxed block per node
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // ── Node header row: Move Up / Move Down / Remove ─────────────────
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Message {index + 1}", EditorStyles.miniBoldLabel, GUILayout.ExpandWidth(true));

            bool moveUp   = GUILayout.Button("▲", GUILayout.Width(24));
            bool moveDown = GUILayout.Button("▼", GUILayout.Width(24));
            bool remove   = GUILayout.Button("✕", GUILayout.Width(24));
            EditorGUILayout.EndHorizontal();

            // ── Sender popup (constrained by channel) ────────────────────────
            DialogueSpeaker[] allowed = AllowedSpeakers(channel);

            // Coerce sender if no longer valid for this channel
            int senderIndex = System.Array.IndexOf(allowed, node.sender);
            if (senderIndex < 0) senderIndex = 0;

            string[] senderNames = System.Array.ConvertAll(allowed, s => s.ToString());
            int newSenderIndex = EditorGUILayout.Popup("Sender", senderIndex, senderNames);
            DialogueSpeaker newSender = allowed[newSenderIndex];

            // ── Payload type popup ────────────────────────────────────────────
            bool isPlayer = newSender == DialogueSpeaker.Player;

            string[] payloadOptions = isPlayer
                ? new[] { "Content", "Sticker", "Choice" }
                : new[] { "Content", "Sticker" };

            int currentPayloadType = PayloadTypeIndex(node.payload, isPlayer);
            int newPayloadType = EditorGUILayout.Popup("Payload Type", currentPayloadType, payloadOptions);

            // ── Expression popup (Azi / Bob only) ────────────────────────────
            string newExpressionId = node.expressionId;
            if (newSender == DialogueSpeaker.Azi || newSender == DialogueSpeaker.Bob)
            {
                CharacterProfileSO profile = ProfileFor(newSender);
                if (profile != null && profile.expressions != null && profile.expressions.Count > 0)
                {
                    // Build options: "(default)" at index 0, then each expressionID
                    var expOptions = new List<string> { "(default)" };
                    foreach (var expr in profile.expressions)
                        expOptions.Add(expr.expressionID);

                    string[] expArr = expOptions.ToArray();

                    int expIndex = string.IsNullOrEmpty(node.expressionId)
                        ? 0
                        : expOptions.IndexOf(node.expressionId);
                    if (expIndex < 0) expIndex = 0;

                    int newExpIndex = EditorGUILayout.Popup("Expression", expIndex, expArr);
                    newExpressionId = newExpIndex == 0 ? "" : expArr[newExpIndex];
                }
                else
                {
                    // Fallback: plain text field when no profile asset exists
                    newExpressionId = EditorGUILayout.TextField("Expression ID", node.expressionId ?? "");
                }
            }

            // ── Apply node-level mutations ────────────────────────────────────
            bool nodeDirty = false;

            if (newSender != node.sender)
            {
                Undo.RecordObject(target, "Change Node Sender");
                node.sender = newSender;
                nodeDirty = true;
                // If sender changed away from Player, strip Choice payload
                if (newSender != DialogueSpeaker.Player && node.payload is ChoicePayload)
                {
                    node.payload = new ContentPayload();
                    newPayloadType = 0; // reset so the block below is consistent
                }
            }

            if (newExpressionId != node.expressionId)
            {
                Undo.RecordObject(target, "Change Expression");
                node.expressionId = newExpressionId;
                nodeDirty = true;
            }

            // Swap payload type if changed
            int resolvedPayloadType = newSender == DialogueSpeaker.Player ? newPayloadType : Mathf.Min(newPayloadType, 1);
            if (resolvedPayloadType != PayloadTypeIndex(node.payload, isPlayer))
            {
                Undo.RecordObject(target, "Change Payload Type");
                node.payload = CreatePayload(resolvedPayloadType, isPlayer);
                nodeDirty = true;
            }

            // ── Payload fields ────────────────────────────────────────────────
            EditorGUILayout.Space(4);
            nodeDirty |= DrawPayloadFields(node, channel, depth);

            if (nodeDirty) EditorUtility.SetDirty(target);

            EditorGUILayout.EndVertical(); // end helpBox

            // Report the requested structural action; DrawNodeList applies it after the
            // loop so the list is never mutated mid-render.
            if (remove)   return NodeAction.Remove;
            if (moveUp)   return NodeAction.MoveUp;
            if (moveDown) return NodeAction.MoveDown;
            return NodeAction.None;
        }

        // ─── payload field drawing ───────────────────────────────────────────────

        /// <summary>Returns true if any field changed.</summary>
        private bool DrawPayloadFields(MessageNode node, DialogueChannel channel, int depth)
        {
            bool dirty = false;

            if (node.payload is ContentPayload cp)
            {
                EditorGUILayout.LabelField("Body");
                string newBody = EditorGUILayout.TextArea(cp.body ?? "", GUILayout.MinHeight(48));
                if (newBody != cp.body)
                {
                    Undo.RecordObject(target, "Edit Message Body");
                    cp.body = newBody;
                    dirty = true;
                }
            }
            else if (node.payload is StickerPayload sp)
            {
                var newSticker = (StickerSO)EditorGUILayout.ObjectField(
                    "Sticker", sp.sticker, typeof(StickerSO), allowSceneObjects: false);
                if (newSticker != sp.sticker)
                {
                    Undo.RecordObject(target, "Change Sticker");
                    sp.sticker = newSticker;
                    dirty = true;
                }
            }
            else if (node.payload is ChoicePayload choicePl)
            {
                dirty |= DrawChoicePayload(choicePl, channel, depth);
            }

            return dirty;
        }

        private bool DrawChoicePayload(ChoicePayload payload, DialogueChannel channel, int depth)
        {
            bool dirty = false;

            EditorGUILayout.LabelField("Options", EditorStyles.miniBoldLabel);

            int removeAt = -1;

            for (int oi = 0; oi < payload.options.Count; oi++)
            {
                var option = payload.options[oi];

                float indentPx = depth * 16f + 8f;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(indentPx);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Option {oi + 1}", EditorStyles.miniBoldLabel, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Remove Option", GUILayout.Width(100)))
                    removeAt = oi;
                EditorGUILayout.EndHorizontal();

                // ── Label payload type ──────────────────────────────────────
                bool isContentLabel = option.label == null || option.label is ContentPayload;
                string[] labelTypes = { "Content", "Sticker" };
                int currentLabelType = isContentLabel ? 0 : 1;
                int newLabelType = EditorGUILayout.Popup("Label Type", currentLabelType, labelTypes);

                if (newLabelType != currentLabelType)
                {
                    Undo.RecordObject(target, "Change Option Label Type");
                    option.label = newLabelType == 0 ? (MessagePayload)new ContentPayload() : new StickerPayload();
                    dirty = true;
                }

                // ── Label content field ─────────────────────────────────────
                if (option.label == null) option.label = new ContentPayload();

                if (option.label is ContentPayload lcp)
                {
                    EditorGUILayout.LabelField("Label Body");
                    string newBody = EditorGUILayout.TextArea(lcp.body ?? "", GUILayout.MinHeight(36));
                    if (newBody != lcp.body)
                    {
                        Undo.RecordObject(target, "Edit Option Label Body");
                        lcp.body = newBody;
                        dirty = true;
                    }
                }
                else if (option.label is StickerPayload lsp)
                {
                    var newSticker = (StickerSO)EditorGUILayout.ObjectField(
                        "Label Sticker", lsp.sticker, typeof(StickerSO), allowSceneObjects: false);
                    if (newSticker != lsp.sticker)
                    {
                        Undo.RecordObject(target, "Change Option Label Sticker");
                        lsp.sticker = newSticker;
                        dirty = true;
                    }
                }

                // ── Child thread ────────────────────────────────────────────
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Child Thread", EditorStyles.miniLabel);

                DrawNodeList(option.children, channel, depth + 1);

                EditorGUILayout.Space(2);
                if (GUILayout.Button("+ New Message (in option)"))
                {
                    Undo.RecordObject(target, "Add Option Child Message");
                    var child = new MessageNode
                    {
                        sender  = DefaultSenderForChannel(channel),
                        payload = new ContentPayload()
                    };
                    option.children.Add(child);
                    dirty = true;
                    EditorUtility.SetDirty(target);
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2);
            }

            // Handle deferred remove
            if (removeAt >= 0)
            {
                Undo.RecordObject(target, "Remove Choice Option");
                payload.options.RemoveAt(removeAt);
                dirty = true;
                EditorUtility.SetDirty(target);
            }

            EditorGUILayout.Space(2);
            if (GUILayout.Button("+ Add Option"))
            {
                Undo.RecordObject(target, "Add Choice Option");
                payload.options.Add(new ChoiceOption { label = new ContentPayload() });
                dirty = true;
                EditorUtility.SetDirty(target);
            }

            return dirty;
        }

        // ─── helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Maps the current payload instance to its popup index.
        /// Index 0 = Content, 1 = Sticker, 2 = Choice (player only).
        /// Defaults to 0 for null.
        /// </summary>
        private static int PayloadTypeIndex(MessagePayload payload, bool isPlayer)
        {
            if (payload is StickerPayload) return 1;
            if (isPlayer && payload is ChoicePayload) return 2;
            return 0; // ContentPayload or null
        }

        /// <summary>Creates a fresh payload instance from popup index.</summary>
        private static MessagePayload CreatePayload(int index, bool isPlayer)
        {
            if (index == 1) return new StickerPayload();
            if (index == 2 && isPlayer) return new ChoicePayload();
            return new ContentPayload();
        }

        /// <summary>Returns a sensible default sender for newly created nodes.</summary>
        private static DialogueSpeaker DefaultSenderForChannel(DialogueChannel channel)
        {
            switch (channel)
            {
                case DialogueChannel.Azi:    return DialogueSpeaker.Azi;
                case DialogueChannel.Bob:    return DialogueSpeaker.Bob;
                case DialogueChannel.Worker: return DialogueSpeaker.Worker;
                default:                     return DialogueSpeaker.Player;
            }
        }
    }
}
