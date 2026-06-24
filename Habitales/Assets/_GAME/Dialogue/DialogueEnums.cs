namespace Habitales.Dialogue
{
    // How a conversation enters the chat. DailyRoll = picked by the worker randomizer.
    // Other trigger types are stubbed for future event/script-driven sends.
    public enum DialogueTrigger
    {
        DailyRoll
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
