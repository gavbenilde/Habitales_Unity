namespace Habitales.Dialogue
{
    // How a conversation enters the chat. DailyRoll = picked by the worker randomizer.
    // Manual = only ever played by direct reference (check-ins, reports, popup links);
    // never rolled or auto-sent, and must NOT be listed in the DialogueRegistry.
    public enum DialogueTrigger
    {
        DailyRoll,
        Manual
    }

    // Which tab a conversation is delivered to. GroupChat/Azi/Bob are hard-installed
    // fixed tabs; Worker routes into a concrete worker's per-name DM tab at send time.
    public enum DialogueChannel
    {
        GroupChat,
        Azi,
        Bob,
        Worker
    }

    // Who authored a given message. Worker resolves to the channel-owner worker's
    // name/portrait at send time (or a temp worker if used inside GroupChat).
    public enum DialogueSpeaker
    {
        Player,
        Azi,
        Bob,
        Worker
    }
}
