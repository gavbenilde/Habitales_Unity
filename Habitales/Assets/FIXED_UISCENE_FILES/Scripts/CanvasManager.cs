using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class CanvasManager : MonoBehaviour
{
    private List<CanvasBase> menuList = new List<CanvasBase>();
    public static CanvasManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void RegisterMenu(CanvasBase canvas)
    {
        menuList.Add(canvas);
        canvas.Hide();
        Debug.Log("hi");
    }

    public CanvasBase GetCanvas(MenuName menu)
    {
        foreach (CanvasBase canvas in menuList)
        {
            if (canvas.Menu == menu)
            {
                return canvas;
            }
        }

        return null;
    }

    public void HideAll()
    {
        foreach (CanvasBase canvas in menuList)
        {
            canvas.Hide();
        }
    }

    public void ShowMenu(MenuName menu)
    {
        HideAll();
        foreach (CanvasBase canvas in menuList)
        {
            if (canvas.Menu == menu)
            {
                canvas.Show();
                break;
            }
        }
    }

    public void AddMenu(MenuName menu)
    {
        foreach (CanvasBase canvas in menuList)
        {
            if (canvas.Menu == menu)
            {
                canvas.Show();
                break;
            }
        }
    }

    public void HideMenu(MenuName menu)
    {
        foreach (CanvasBase canvas in menuList)
        {
            if (canvas.Menu == menu)
            {
                canvas.Hide();
                break;
            }
        }
    }
    
}
