using Solace.LauncherUI;

namespace Solace.LauncherUI.Tests;

public class ServerManagerTests
{
    [Test]
    public async Task RefreshComponentStatuses_AllOffline_SetsOffline()
    {
        var manager = new ServerManager();
        foreach (var component in manager.Components)
        {
            component.Status = ServerStatus.Offline;
        }

        manager.RefreshComponentStatuses(detectRunning: false);

        await Assert.That(manager.Status).IsEqualTo(ServerStatus.Offline);
        await Assert.That(manager.AnyOnline).IsFalse();
        await Assert.That(manager.CanStart).IsTrue();
        await Assert.That(manager.CanStop).IsFalse();
    }

    [Test]
    public async Task RefreshComponentStatuses_PartiallyOnline_SetsPartiallyOnline()
    {
        var manager = new ServerManager();
        manager.Components[0].Status = ServerStatus.Online;
        manager.Components[1].Status = ServerStatus.Offline;

        manager.RefreshComponentStatuses(detectRunning: false);

        await Assert.That(manager.Status).IsEqualTo(ServerStatus.PartiallyOnline);
        await Assert.That(manager.AnyOnline).IsTrue();
    }

    [Test]
    public async Task AcquireStartLock_ReturnsNullWhenOffline()
    {
        var manager = new ServerManager();
        foreach (var component in manager.Components)
        {
            component.Status = ServerStatus.Offline;
        }

        manager.RefreshComponentStatuses(detectRunning: false);
        var lockHandle = manager.AcquireStartLock();

        await Assert.That(lockHandle).IsNull();
    }

    [Test]
    public async Task AcquireStartLock_ReturnsHandleWhenNotOffline()
    {
        var manager = new ServerManager();
        manager.Components[0].Status = ServerStatus.Online;
        manager.Components[1].Status = ServerStatus.Offline;

        manager.RefreshComponentStatuses(detectRunning: false);
        var lockHandle = manager.AcquireStartLock();

        await Assert.That(lockHandle).IsNotEqualTo(null);
        await Assert.That(manager.StartLocked).IsTrue();

        lockHandle?.Dispose();
        await Assert.That(manager.StartLocked).IsFalse();
    }

    [Test]
    public async Task RefreshComponentStatuses_StoppingRemainsStopping()
    {
        var manager = new ServerManager();
        manager.Components[0].Status = ServerStatus.Online;
        manager.Components[1].Status = ServerStatus.Stopping;

        manager.RefreshComponentStatuses(detectRunning: false);

        await Assert.That(manager.Status).IsEqualTo(ServerStatus.Stopping);
    }

    [Test]
    public async Task CanRestart_IsFalseWhenOffline()
    {
        var manager = new ServerManager();
        foreach (var component in manager.Components)
        {
            component.Status = ServerStatus.Offline;
        }

        manager.RefreshComponentStatuses(detectRunning: false);

        await Assert.That(manager.CanRestart).IsFalse();
    }
}
