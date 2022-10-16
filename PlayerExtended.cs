using UnityEngine;

namespace AutomaticSaves
{
    public class PlayerExtended : Player
    {
        protected override void Start()
        {
            base.Start();
            new GameObject("__AutomaticSavesMod__").AddComponent<AutomaticSaves>();
        }
    }
}
