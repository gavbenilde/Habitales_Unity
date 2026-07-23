using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum MenuName
{ 
    MainMenu,
    PauseMenu, 
    ReportMenu,
    ActionMenu,
    ZoneMenu, 

}


public class CanvasBase : MonoBehaviour
{
    [HideInInspector]
    public MenuName Menu
    {
        get => menuName;
    }

    [SerializeField] private MenuName menuName;

    protected virtual void Start()
    {
        CanvasManager.Instance.RegisterMenu(this);
    }

    public void Hide()
    {
        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }
    }

    public void Show()
    {
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
    }
}
