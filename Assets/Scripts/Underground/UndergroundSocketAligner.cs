using UnityEngine;

// moduleSocket이 targetSocket과 마주보게(forward가 서로 반대) moduleToPlace 전체를 옮기고 돌린다.
public static class UndergroundSocketAligner
{
    public static void AlignToSocket(UndergroundModule moduleToPlace, DoorSocket moduleSocket, DoorSocket targetSocket)
    {
        float angle = Vector3.SignedAngle(moduleSocket.transform.forward, -targetSocket.transform.forward, Vector3.up);
        moduleToPlace.transform.RotateAround(moduleSocket.transform.position, Vector3.up, angle);

        moduleToPlace.transform.position += targetSocket.transform.position - moduleSocket.transform.position;
    }
}
