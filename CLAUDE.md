# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**FoundationDI** is a Unity game development framework providing Dependency Injection (DI) and service architecture. It offers reusable systems for UI management, audio, object pooling, and event communication—designed as a foundation for building modular, maintainable game projects.

## Repository Structure

```
Assets/FoundationDI/Runtime/
├── Managers/
│   └── UIManager/              # UI orchestration (pages + popups)
│       ├── UIManager.cs        # Central coordinator
│       ├── UIPresenter.cs      # Presentation logic base class
│       ├── UIView.cs           # View MonoBehaviour base class
│       └── UISetting.cs        # UI configuration asset
├── Services/
│   ├── MessageService.cs       # Event bus (Pub/Sub pattern)
│   ├── PoolService/            # Object pooling system
│   │   ├── PoolService.cs
│   │   ├── PoolData.cs
│   │   └── PoolItem.cs
│   └── SoundService/           # Audio management
│       ├── SoundService.cs
│       └── SoundData.cs
└── Utilities/
    ├── SafeAreaFitter.cs       # Screen notch/safe area handling
    └── SpriteStretcher.cs      # Aspect ratio scaling utility

Assets/Scripts/LifetimeScopes/
└── RootLifetimeScope.cs        # VContainer DI setup
```

## Architecture Overview

FoundationDI uses a **Layered Service-Oriented Architecture**:

1. **Application Layer**: Game-specific UI presenters and game logic
2. **Managers & Services Layer**: UIManager, SoundService, PoolService, MessageService
3. **DI Container Layer**: VContainer (dependency injection) + MessagePipe (pub/sub) + UniTask (async)
4. **Utility Layer**: Helper components for UI and display

### Key Design Patterns

- **Dependency Injection**: VContainer registers and resolves all services
- **Service Locator**: Services accessed via `IObjectResolver.Resolve<T>()`
- **MVP Architecture**: UIPresenter (logic) + UIView (MonoBehaviour UI)
- **State Machine**: UIManager manages pages and popup stack
- **Pub/Sub**: MessageService for decoupled event communication
- **Object Pool**: PoolService reuses objects to reduce allocations
- **Factory Pattern**: Dynamic UI presenter creation via reflection

## Core Systems

### UIManager (UI State Management)

**Purpose**: Manages page navigation and popup stack with input blocking cascade.

**Key Methods**:
```csharp
UIManager.ChangeView(string pageName)           // Change page (async)
UIManager.ShowPopup<T>(string popupName)        // Show popup, blocks input to previous UI
UIManager.HidePopup(IUIPresenter presenter)     // Hide popup, re-enables previous UI
```

**Architecture**:
- One active page at a time
- Popup stack (multiple popups allowed)
- Input blocking: popups disable input for underlying UI
- Lazy loading: UI prefabs loaded on first request
- Lifecycle: Initialize → Show → Hide → Close

**To Create a New UI Page**:
1. Create a MonoBehaviour class extending `UIView<TPresenter>`
2. Create a Presenter class extending `UIPresenter<TView>`
3. Add to `UISetting` asset configuration
4. Call `UIManager.ChangeView("PageName")`

### PoolService (Object Pooling)

**Purpose**: Reuses objects to reduce memory allocations and improve performance.

**Key Methods**:
```csharp
PoolService.Get(string key)                     // Get pooled object
PoolService.Get<T>(string key) where T : IPoolItem  // Get typed object
PoolService.Release(IPoolItem item)             // Return to pool
```

**Features**:
- Hybrid asset loading (Resources + Addressables)
- Automatic asset caching
- Automatic PoolItem component attachment if missing
- Supports any prefab with IPoolItem interface

### SoundService (Audio Management)

**Purpose**: Manages BGM and SFX playback with volume control and state persistence.

**Key Methods**:
```csharp
SoundService.PlayBGM(string key)                // Play background music
SoundService.PlaySFX(string key)                // Play sound effect
SoundService.StopBGM()                          // Stop current BGM
SoundService.SetBGMVolume(float volume)         // Set BGM volume (0-1)
SoundService.SetSFXVolume(float volume)         // Set SFX volume (0-1)
```

**Features**:
- Separate BGM (single) and SFX (pool) players
- Volume persistence via PlayerPrefs
- Enable/disable toggles for BGM and SFX
- Prevents duplicate SFX in same frame
- R3 Observable integration

### MessageService (Event Bus)

**Purpose**: Decoupled pub/sub communication between systems.

**Key Methods**:
```csharp
MessageService.Publish<T>(T message)            // Publish message
MessageService.Subscribe<T>(Action<T> handler)  // Subscribe to messages
MessageService.SubscribeAsync<T>(Func<T, CancellationToken, UniTask> handler)
```

**Usage Example**:
```csharp
// Publishing
messageService.Publish<PlayerDied>(new PlayerDied { Level = 3 });

// Subscribing
messageService.Subscribe<PlayerDied>(msg =>
{
    Debug.Log($"Player died at level {msg.Level}");
});
```

## Dependencies

**Key NuGet/Git Packages**:
- `jp.hadashikick.vcontainer` - Dependency injection container
- `com.cysharp.messagepipe` - Pub/sub event system
- `com.cysharp.unitask` - Async/await support
- `com.cysharp.r3` - Reactive programming (Rx)
- `com.darknaku.director` - Animation/sequencing framework
- `com.kyrylokuzyk.primetween` - Tweening library

**Unity Packages**:
- `com.unity.addressables` - Asset management
- `com.unity.inputsystem` - Modern input handling
- `com.unity.render-pipelines.universal` - URP rendering

## Common Development Tasks

### Build and Compilation

```bash
# Unity Editor: Build → Build Settings → Build
# VS Code: Use dotnet CLI for console builds
dotnet build FoundationDI.csproj
```

### Running Unit Tests

```bash
# In Unity: Window → General → Test Runner
# Or via CLI
dotnet test
```

### Adding a New Service

1. Create service interface in `Services/`
2. Implement service class
3. Register in `RootLifetimeScope.cs`: `builder.Register<IMyService, MyService>(Lifetime.Singleton)`
4. Inject via constructor in other classes

### Adding a New UI Page

1. Create view script extending `UIView<TPresenter>` in Assets
2. Create presenter script extending `UIPresenter<TView>`
3. Create/configure prefab with UIView component
4. Add entry to `UISetting` asset
5. Navigate using: `UIManager.ChangeView("PageName")`

### Working with Pooled Objects

1. Implement `IPoolItem` interface (or attach `PoolItem` MonoBehaviour)
2. Place prefab in Resources folder or configure in Addressables
3. Get object: `PoolService.Get<MyObject>("prefabKey")`
4. Return to pool: `PoolService.Release(myObject)`

## Important Implementation Notes

### UI Presenter Type Resolution

UIPresenter types are discovered via reflection in `AppDomain.CurrentDomain.GetAssemblies()`. This means:
- All UIPresenter subclasses must be in loaded assemblies
- UIPresenter name must match page name configuration
- Type discovery happens at runtime

### Asset Loading Strategy

Services use a hybrid approach:
1. First checks asset cache (dictionary lookup)
2. Then tries Resources folder
3. Falls back to Addressables

Ensure assets are properly placed in Resources folders or configured in Addressables for hybrid loading.

### Input Blocking Cascade

When showing popups:
- Previous popup (or page) gets `InputEnabled = false`
- When hiding popup, re-enables the previous UI
- This is manual state management—ensure consistent enable/disable calls

### R3 and ReactiveX

The framework uses R3 (C# Rx implementation) for reactive patterns. When subscribing to observables:
```csharp
observable.Subscribe(x => Debug.Log(x)).AddTo(gameObject); // Automatic cleanup
```

The `.AddTo(gameObject)` pattern ensures subscription is disposed when object is destroyed.

## Code Style and Conventions

- **Namespace**: `FoundationDI` for framework code
- **Interfaces**: Start with `I` (e.g., `IUIManager`)
- **Async Methods**: Prefix with `Async` (e.g., `AsyncChangeTo`)
- **Generics**: Used extensively for type safety (e.g., `UIView<TPresenter>`)
- **Services**: Registered as Singletons with interfaces
- **Presenters**: Inherit from `UIPresenter<TView>` for type safety

## Recent Changes

Recent commits show focus on:
- Type resolution improvements for UIPresenter discovery
- Direct UIPresenter instantiation (avoiding reflection in some cases)
- Utility additions (SafeAreaFitter, SpriteStretcher)
- SoundService implementation
- UIManager and PoolService foundation

## Performance Considerations

1. **Object Pooling**: Always release pooled objects back via `PoolService.Release()`
2. **UI Transitions**: Use `UniTask` for async transitions, call `.Forget()` for fire-and-forget operations
3. **Asset Caching**: Services cache loaded assets—be aware of memory implications for large projects
4. **Duplicate SFX**: SoundService prevents identical SFX playing multiple times per frame

## Testing Strategy

- Services should be testable via dependency injection mocking
- Create mock implementations of service interfaces
- Use constructor injection for all dependencies
- Avoid Service Locator pattern in game code (use constructor injection instead)

## Common Pitfalls to Avoid

1. **Not Implementing IPoolItem**: Pooled objects must implement IPoolItem or have PoolItem component attached
2. **Forgetting to Release**: Always call `PoolService.Release()` when done with pooled objects
3. **Direct Service Access**: Avoid static service access—inject via constructor using VContainer
4. **UI State Sync**: Ensure InputEnabled is properly managed when showing/hiding popups
5. **Asset Path Issues**: Double-check Resources folder paths and Addressables configuration for asset loading
6. **Type Name Mismatch**: UIPresenter class names must match configuration names for reflection-based discovery

## Future Extensibility Points

- **Custom Services**: Add new services following the existing patterns (interface + implementation + VContainer registration)
- **Custom Animators**: Extend `UIView` lifecycle hooks (OnEnterBefore, OnEnterAfter, etc.) for custom animations
- **New Asset Managers**: Follow PoolService pattern for other asset types (Sprites, Textures, etc.)
- **Save System**: Build on MessageService pattern for game state persistence
