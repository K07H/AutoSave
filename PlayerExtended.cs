using UnityEngine;

namespace AutoSave
{
    public class PlayerExtended : Player
    {
        protected override void Start()
        {
            base.Start();
            new GameObject("__AutoSaveMod__").AddComponent<AutoSave>();
        }
    }
}
