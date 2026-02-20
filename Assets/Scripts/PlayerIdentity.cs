using UnityEngine;

namespace WormCrawlerPrototype
{
    public sealed class PlayerIdentity : MonoBehaviour
    {
        [SerializeField] private string playerName = "Player";
        [SerializeField] private int playerIndex = 0;
        [SerializeField] private int teamIndex = 0;

        public string PlayerName
        {
            get => playerName;
            set => playerName = value;
        }

        public int PlayerIndex
        {
            get => playerIndex;
            set => playerIndex = value;
        }

        public int TeamIndex
        {
            get => teamIndex;
            set => teamIndex = value;
        }
    }
}
