using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UI;

public class StatusIconUI : MonoBehaviour
{
    public Image icon;
    public Image bar;

    private void Awake()
    {
        bar.material = new Material(bar.material);
    }
}
