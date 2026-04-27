using UnityEngine;

[System.Serializable]
public class UIProperites
{
    public string name;
    public string description;
    public string value;
}

[System.Serializable]
public class UISegmentStarInfo : UIProperites
{
    public Color BackgroundColor = Color.black;
    public Color BorderColor = Color.red;
    public Color IconColor = Color.white;
    public Sprite Icon;
}
