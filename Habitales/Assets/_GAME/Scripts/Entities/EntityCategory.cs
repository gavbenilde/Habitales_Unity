// Entity category — replaces `is VillageEntity` type-checks with `def.category == ...`
// checks (arch §5.1). Identity is `entityId`; category is for broad behavioural grouping.
namespace Habitales.Entities
{
    public enum EntityCategory
    {
        Plant,
        Building,
        Hazard,
        Debris
    }
}
