public interface IDamageable
{
    bool IsDowned { get; }
    void TakeDamage(float amount);
}
