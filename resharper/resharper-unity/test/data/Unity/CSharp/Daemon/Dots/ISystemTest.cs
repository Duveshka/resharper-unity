﻿using System;
using Unity.Entities;

namespace Unity.Entities
{
    public interface ISystem {}
    public struct SystemState {}
}

public partial struct SettingsSystem : ISystem
{
  public void OnCreate(ref SystemState state)
  {
     throw new NotImplementedException();
  }

  public void OnDestroy(ref SystemState state)
  {
  }

  public void OnUpdate(ref SystemState state)
  {
     DoThing();
  }

  private void DoThing()
  {
  }
}