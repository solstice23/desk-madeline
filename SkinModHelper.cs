namespace DeskMadeline
{
    /// <summary>
    /// What SkinModHelper patches into the player while a skin of its kind is worn: its hook on
    /// Sprite.Play, and its IL edits to the Player constructor's idle lambda. Null in vanilla.
    /// </summary>
    public interface IPlayerSkin
    {
        /// <summary>SomePatches.PlayerSpritePlayHook.</summary>
        void Play(Player player, string id, bool restart, bool randomizeFrame);

        /// <summary>CharacterConfig.IdleAnimationChance, standing in for the lambda's 0.2.</summary>
        float? IdleAnimationChance { get; }

        /// <summary>CharacterConfig.IdleColdOptions, standing in for both the cold and the no-backpack sets.</summary>
        Chooser IdleColdOptions { get; }

        /// <summary>ilPlayer_b__280_2's first get_Mode: a skin with its backpack chooses as Madeline.</summary>
        int PatchModeIdleOptions(int mode);

        /// <summary>_patchSpriteMode_NB: every other get_Mode in those lambdas.</summary>
        int PatchModeNoBackpack(int mode);
    }
}
