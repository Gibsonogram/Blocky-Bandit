// Implemented by actors that sense the board (line-of-sight, target tracking) during
// their turn. TurnManager calls ReactToExplosion after a blast resolves so their state
// reflects the settled board this same turn. This updates sensing/state only; it must
// not move the actor, since movement already happened in TakeTurn.
public interface IExplosionReactor
{
    void ReactToExplosion();
}
