using UnityEngine;

public class TriggerDoor : MonoBehaviour
{
    public Transform door;          // Дверь которую двигаем
    public float moveDistance = 2f; // Насколько открыть
    public float speed = 2f;        // Скорость

    public enum Direction { Up, Forward, Right }
    public Direction direction = Direction.Right;

    private Vector3 closedPos;
    private Vector3 openPos;
    private bool isOpen = false;

    void Start()
    {
        closedPos = door.position;

        Vector3 dir = Vector3.right;

        switch (direction)
        {
            case Direction.Up: dir = transform.up; break;
            case Direction.Forward: dir = transform.forward; break;
            case Direction.Right: dir = transform.right; break;
        }

        openPos = closedPos + dir * moveDistance;
    }

    void Update()
    {
        if (door == null) return;

        Vector3 target = isOpen ? openPos : closedPos;

        door.position = Vector3.MoveTowards(
            door.position,
            target,
            speed * Time.deltaTime
        );
    }

    void OnTriggerEnter(Collider other)
    {
        isOpen = true;
    }

    void OnTriggerExit(Collider other)
    {
        isOpen = false;
    }
}