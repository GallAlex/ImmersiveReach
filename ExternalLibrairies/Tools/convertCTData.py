from skimage import measure
import nibabel as nib
import sys

def convertCTData(inputPath, outputPath):
    # Load the CT scan
    img = nib.load(inputPath)
    data = img.get_fdata()
    print("Loaded CT data from " + inputPath)

    # Marching cubes
    verts, faces, normals, values = measure.marching_cubes(data, 0)
    faces += 1  # Faces are 0-indexed, we need to add 1 to match the OBJ format
    print("Marching cubes done")

    # Save the result
    with open(outputPath, 'w') as f:
        for item in verts:
            f.write("v {0} {1} {2}\n".format(item[0],item[1],item[2]))
        for item in normals:
            f.write("vn {0} {1} {2}\n".format(item[0],item[1],item[2]))
        for item in faces:
            f.write("f {0}//{0} {1}//{1} {2}//{2}\n".format(item[0],item[1],item[2]))

# read command line arguments
inputPath = sys.argv[1]
fileName = inputPath.split("/")[-1].split(".")[0]

outputFolder = "../../Assets/_CrepuscularRays/Resources/CTData/"
outputPath = outputFolder + fileName + ".obj"

convertCTData(inputPath, outputPath)
print("Converted CT data to OBJ format and saved it to " + outputPath)


