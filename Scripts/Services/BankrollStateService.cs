using Godot;

public partial class BankrollStateService : Node
{
	private const decimal DefaultInitialBalance = 1.00000000m;
	private bool _initialized;

	public decimal CurrentBalance { get; private set; } = DefaultInitialBalance;

	public void EnsureInitialized(decimal fallbackInitialBalance = DefaultInitialBalance)
	{
		if (_initialized)
		{
			return;
		}

		CurrentBalance = fallbackInitialBalance > 0m ? fallbackInitialBalance : DefaultInitialBalance;
		_initialized = true;
	}

	public void SetBalance(decimal balance)
	{
		CurrentBalance = balance;
		_initialized = true;
	}
}
