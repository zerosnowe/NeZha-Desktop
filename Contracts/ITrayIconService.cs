namespace NeZha_Desktop.Contracts;

public interface ITrayIconService : IDisposable
{
    void Initialize(MainWindow window);
}
