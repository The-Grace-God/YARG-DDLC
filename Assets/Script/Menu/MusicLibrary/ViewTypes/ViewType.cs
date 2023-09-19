using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YARG.Menu.MusicLibrary
{
    public abstract class ViewType
    {
        public enum BackgroundType
        {
            Normal,
            Category
        }

        public abstract BackgroundType Background { get; }

        public abstract string PrimaryText { get; }
        public virtual string SecondaryText => string.Empty;
        public virtual bool UseAsMadeFamousBy => false;

        public virtual string SideText => string.Empty;

        public virtual UniTask<Sprite> GetIcon()
        {
            return UniTask.FromResult<Sprite>(null);
        }

        public virtual void SecondaryTextClick()
        {
        }

        public virtual void PrimaryButtonClick()
        {
        }

        public virtual void IconClick()
        {
        }
    }
}